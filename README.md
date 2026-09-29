# MapleAuctionSniper

MapleStory Worlds「楓星」拍賣場監控工具的基礎專案。目標是從畫面辨識裝備、套用使用者條件與歷史價格策略，通知使用者自行購買。目前裝備與歷史使用虛構資料；啟用通知後可實際透過 Discord Webhook 發送。不連接遊戲、不自動購買、不讀取 process memory、不注入 DLL、不處理遊戲封包，也沒有寫死畫面座標。

## 技術與執行環境

- Windows、WPF、.NET 10、C# 14（穩定版，無 preview 功能）。
- `global.json` 要求 SDK 10.0.301 以上的 .NET 10 SDK，允許升級到較新的 feature band；建議安裝[官方最新穩定版 .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。初始化機器的 SDK 是 10.0.301。
- 正式專案不使用第三方 NuGet 套件；測試只使用 xUnit、Visual Studio runner 與 Microsoft.NET.Test.Sdk。
- Constructor injection；`App.OnStartup` 是組裝實作的 composition root，不使用 service locator 或可變 static 全域狀態。

## Solution 結構

```text
MapleAuctionSniper.sln
Directory.Build.props
global.json
src/
  MapleAuctionSniper.Domain/          純模型、條件匹配、低價策略
  MapleAuctionSniper.Application/     掃描 use case、技術邊界 contracts
  MapleAuctionSniper.Infrastructure/  JSON 模擬辨識、本機設定、記憶體歷史、Discord HTTP
  MapleAuctionSniper.Desktop/         WPF、簡單 MVVM、實作組裝
tests/
  MapleAuctionSniper.Domain.Tests/
  MapleAuctionSniper.Application.Tests/
```

依賴方向：Application → Domain；Infrastructure → Application；Desktop → Infrastructure（並使用傳遞參考的 Application／Domain）。Domain 無專案依賴。Desktop code-behind 只初始化視窗；條件判定、中位數演算法與持久化流程不在 UI。

## 模型與低價規則

- `Price`：非負 decimal 金額，目前只有一種拍賣貨幣。
- `Equipment`／`EquipmentStats`：名稱、STR、DEX、INT、LUK、WeaponAttack、MagicAttack、UpgradeSlots；另有 optional RequiredLevel／Rarity。
- `EquipmentSearchAttributes`：道具大類、兩層分類、潛在能力、附加潛在能力。分類與潛能採可擴充標籤；辨識缺失以 null 表示，不會被視為符合指定條件。
- `AuctionListing`：來源辨識 ID、裝備、價格、觀測時間。
- `AuctionSearchFilters`／`PriceRange`／`SearchCriteria`：大類、兩層分類、潛能、名稱、等級與價格上下限；基本條件之間採 AND，範圍包含等號，留空不限。名稱可完全符合或包含。
- `DetailSearch`／`MinimumStatCondition`：最多三組「素質＋最小值」，採 AND／OR；零組條件代表不限，OR 不會放寬分類、名稱、等級或價格。原有 `StatCriteria` 仍可供程式端設定素質上下限，UI 改用遊戲截圖的三列詳細條件。
- `MonitoringRule`：條件與 `DiscountThreshold`（必須 > 0 且 <= 1）。
- `DealEvaluation`：條件是否符合、是否 Potential Deal、歷史中位數與原因。
- Application 的 `SearchPreset`／`ISearchPresetRepository`：具名稱的監控規則與儲存邊界；Infrastructure 的 `JsonSearchPresetRepository` 提供本機 JSON 實作，Domain 不引用 JSON 或檔案 API。

`IDealDetectionStrategy` 是可替換的商業策略邊界。第一版 `HistoricalMedianDealStrategy` 同時要求搜尋條件符合、有效歷史中位數 > 0，以及 `ListingPrice <= HistoricalMedianPrice * DiscountThreshold`。偶數筆樣本取中間兩筆平均；沒有歷史資料時，仍可能符合搜尋條件，但不判定為低價。

記憶體 repository 只比較**同名稱、同素質、同等級、稀有度、分類與潛能**的較早觀測，排除目前 listing ID，以 ID 去重歷史觀測。這是保守的示範規則；歷史資料代表刊登價格，並非成交價格。真實版還需要市場／伺服器識別、可靠的 listing 身分、樣本數門檻、時間窗與品質比較政策。

## Application workflow

`ScanAuction.ExecuteAsync(rule)` 提供單條件手動掃描；`ExecuteForSearchesAsync(searches)` 提供多條件共用掃描：

1. `IScreenCapture` 取得 `CapturedFrame`（不含 Windows 型別的 opaque bytes、media type、時間）。
2. `IAuctionScreenAnalyzer` 轉換成裝備刊登資料。
3. 對每筆讀取一次 repository 的歷史可比價格，用 domain strategy 評估每組搜尋條件。
4. 全批判斷完成後保存 `AuctionSnapshot`，避免本次掃描成為自己的比較基準。
5. 任一條件判定為 Potential Deal 即呼叫 `INotificationService`；同筆裝備本輪只通知一次，訊息包含符合低價的條件名稱。回傳包含每組判定、符合條件名稱與通知結果的 `ScanResult`，不因多組條件複製裝備列。

掃描支援 cancellation。讀取、辨識、保存失敗會向上傳遞，由 ViewModel 顯示失敗訊息；保存失敗不通知。通知失敗時快照與掃描結果仍保留，錯誤顯示於該筆 `ListingAnalysis.NotificationError`。這版尚無通知重試／outbox。通知成功不代表使用者已閱讀。

目前 `FakeScreenCapture` 把可注入的 JSON 字串轉成 frame；`FakeAuctionScreenAnalyzer` 解析 JSON，而不是做 OCR。`SimulationData.Create` 提供四筆虛構裝備與三筆中位數為 100,000,000 的歷史資料。也可自行注入其他 JSON，欄位為 `Id`、`Name`、`Price`、`STR`、`DEX`、`INT`、`LUK`、`WeaponAttack`、`MagicAttack`、`UpgradeSlots`；optional 欄位為 `RequiredLevel`、`Category`（`Armor`／`Weapon`／`Consumable`）、`Classification`、`Subclassification`、`Potential`、`AdditionalPotential`。舊 JSON 不提供這些欄位仍可解析。

## 執行與操作

```powershell
dotnet restore MapleAuctionSniper.sln
dotnet build MapleAuctionSniper.sln --no-restore
dotnet run --project src/MapleAuctionSniper.Desktop
```

1. 左側進階搜尋參考使用者提供的手機拍賣場截圖：大類（防具／武器／消耗）、道具分類兩個選單、潛在能力／附加潛在能力、道具名稱、等級範圍、價格範圍、三組詳細搜尋與 AND／OR。
2. 等級與價格的兩個欄位分別是最低／最高，留空表示不限；「全體」代表不限制分類／潛能。詳細搜尋選擇素質後須填最小值，「不選擇」的列不參與判定。折扣使用 `0.8` 格式。
3. 按「建立／更新監控條件」，再按「執行一次模擬掃描」。修改欄位後須再按更新；無效條件會停用掃描。
4. 表格顯示所有辨識項目、歷史中位數、條件匹配、低價判定、通知結果與錯誤。

預設條件：模擬長劍、價格上限 100,000,000、STR >= 10、物攻 >= 90、折扣 0.8。第一次掃描會顯示四筆裝備，其中兩筆符合條件，一筆 75,000,000 的裝備觸發低價；通知預設停用，啟用 Discord 後這筆模擬裝備也會實際發送。95,000,000 的同素質裝備未達折扣門檻；50,000,000 的低素質裝備不符合條件；法杖名稱不符。

潛在能力與附加潛在能力依使用者確認，皆提供固定選項「全體、特殊、稀有、罕見、傳說」，預設全體（不限）；指定等級採完全相同標籤匹配，不包含較高等級。分類與詳細素質選單尚未確認完整內容；分類目前為可輸入的 ComboBox，詳細搜尋先提供已建模的七項素質。模擬長劍的分類／子分類／潛能／附加潛能分別為「模擬分類」「模擬子分類」「特殊」「稀有」，可選擇這些條件驗證匹配。大類預設武器；等級範圍目前對應裝備需求等級。名稱完全符合為額外可選設定；歷史折扣門檻放在獨立的通知設定區。分類聯動與遊戲查詢的精確語意待後續確認；手機截圖僅作欄位參考，沒有用於 OCR、座標或自動操作遊戲。

快照與歷史目前只存在記憶體，關閉程式後消失。Discord 訊息送出後留在頻道中，本機尚未保存發送歷史。重複掃描會保留快照、以 ID 更新觀測並可能在後續輪次重複通知；後續掃描的中位數可能包含先前掃描中其他可比刊登。UI 保留單條件手動掃描，也支援選擇多組已儲存條件進行定時監控。

## 儲存與快速套用搜尋條件

左側頂端的「已儲存的搜尋條件」支援：

1. 編輯搜尋欄位與低價折扣門檻，輸入條件名稱，按「儲存目前條件（同名更新）」。儲存的是目前輸入，不需要先按建立／更新；無效輸入不會寫入檔案。同名（不分大小寫）會更新既有條件。
2. 從清單選擇已儲存條件，按「快速套用」。所有分類、名稱模式、等級／價格範圍、潛能、三組詳細條件、AND／OR 與折扣門檻一併還原；套用後可直接掃描，舊結果清除。
3. 按「刪除」移除選取的儲存條件；目前啟用中的查詢不受影響。

設定保存於 `%LOCALAPPDATA%\MapleAuctionSniper\search-presets.json`，程式啟動時自動讀取，不需要資料庫或帳號。清單有版本欄位；先寫同目錄暫存檔，再替換正式檔。讀取或寫入失敗會顯示訊息；損壞或不支援的版本不會被自動清空／覆寫，仍可使用模擬掃描。這版以單一程式視窗操作為使用情境，尚無跨執行個體同步。

## 指定分鐘間隔與多道具監控

1. 在左側建立各道具的條件，分別命名儲存（例如「長劍」「法杖」）。同一道具也可有多組不同素質／價格條件。
2. 在右側「定時監控多個道具」勾選要監控的已儲存條件。
3. 填入搜尋間隔，預設 `5` 分鐘，可使用小數（例如 `2.5`），必須大於零且最多 `1440` 分鐘。
4. 按「開始監控」立即執行第一輪；每輪結束後等待設定的分鐘數再執行下一輪。耗時較久時不會重疊，也不會補跑累積輪次。
5. 畫面顯示已完成輪數、下次掃描時間與每筆裝備符合的條件名稱。按「停止監控」可取消等待或進行中的掃描；停止後可修改間隔、條件、勾選項目再重新啟動。

監控時停用手動掃描與搜尋條件修改，避免並行操作；通知設定仍可更改並供後續通知使用。擷取／辨識失敗會顯示訊息，等待設定間隔後繼續下一輪。通知失敗則保留該輪辨識結果。每輪共用一次擷取／辨識及一份快照，各道具條件皆在同一批資料上評估；目前仍使用模擬資料，未控制遊戲搜尋畫面。

開始時會將間隔與勾選名稱保存到 `%LOCALAPPDATA%\MapleAuctionSniper\monitoring-schedule.json`。重開後還原設定，但不自動啟動；找不到的條件名稱會略過。程式必須保持開啟才能監控，關閉程式會停止；尚不支援背景 Windows service 或跨輪通知去重。

Application 的 `ScheduledAuctionMonitor` 使用 constructor 注入的 `TimeProvider` 與可取消的非同步等待，與 WPF／Windows Timer 無關。排程測試使用手動時間與模擬來源，不需要真的等待數分鐘。

## Discord 通知設定

1. 按主視窗右上方「通知設定」。
2. 貼上目標文字頻道的完整 Discord Webhook URL；Webhook 決定接收頻道，無需額外填 channel ID。未預填任何真實憑證。
3. 可按「發送測試通知」測試目前輸入的 URL；此操作會實際發送一則訊息，不需要先啟用，且不會自動保存。
4. 勾選「啟用 Discord 低價通知」並按「儲存」。後續掃描讀取最新設定，不需重開；取消勾選並儲存即可停用。關閉彈窗不會保存未儲存的修改。

通知設定存於 `%LOCALAPPDATA%\MapleAuctionSniper\notification-settings.json`，目前以明文 JSON 保存在本機（包含 Webhook token），不應分享或提交這份檔案。支援一般 `discord.com`／`discordapp.com` 的 HTTPS Webhook URL，可包含 API 版本；不接受頻道連結、任意主機、query、fragment 或非標準連接埠。現階段只針對一般文字頻道，尚不支援 forum／thread 參數。

`INotificationSettingsRepository` 與 `IDiscordWebhookSender` 是 Application 的技術邊界，`DiscordNotificationService` 格式化裝備、價格、歷史中位數、素質與判定原因。Infrastructure 以 `JsonNotificationSettingsRepository` 保存設定，`DiscordWebhookSender` 使用注入的 `HttpClient`。依 [Discord 官方 Webhook 文件](https://discord.com/developers/docs/resources/webhook#execute-webhook)，請求使用 `wait=true` 等待發送確認，並設定空的 `allowed_mentions.parse` 避免裝備文字造成提及通知。

HTTP 逾時設為 15 秒且不自動重新導向；失效／權限／限流／連線錯誤會顯示可讀訊息，不呈現 URL、token 或原始 Discord 回應內容。失敗不會丟掉掃描結果，也不自動重試，以避免不確定的發送結果造成重複訊息。預設停用；記憶體通知 adapter 仍保留供測試替換。

若受限環境不允許 CLI 寫入使用者目錄，可先設定暫存位置，再使用單節點 MSBuild：

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local'
dotnet restore MapleAuctionSniper.sln --disable-parallel -m:1
dotnet build MapleAuctionSniper.sln --no-restore -m:1
dotnet test MapleAuctionSniper.sln --no-build --no-restore -m:1
```

`.local/`、`bin/`、`obj/` 已由 Git 忽略。

## 測試

```powershell
dotnet test MapleAuctionSniper.sln
```

Domain 測試涵蓋價格上限、素質上下限、名稱匹配、歷史中位數（奇／偶數）、折扣邊界、無歷史與輸入驗證。Application 測試涵蓋 JSON 模擬端到端流程、替換 analyzer／repository／strategy、先分析後保存、資料去重與可比性、取消、辨識錯誤與保存錯誤。均不需要遊戲、截圖或外部資料庫。

最新驗證結果（Windows，SDK 10.0.301）：restore 成功，build 為 0 errors／0 warnings；75 個測試全部通過（Domain 27、Application 48），無略過。涵蓋查詢、本機設定、Discord 發送，以及多道具共用擷取／快照、重疊規則每輪只通知一次、立即首輪、指定間隔、慢速掃描不重疊、取消、失敗後續跑與排程設定保存。HTTP 使用模擬 handler，無真實頻道發送。

桌面操作驗證亦通過：WPF 啟動、建立條件前掃描停用、預設掃描顯示 4 筆裝備／1 筆低價／1 筆模擬通知、OR 切換、等級與價格篩選，以及價格下限大於上限時顯示錯誤並停用掃描。新增本機條件的 WPF 檢查涵蓋儲存、套用後欄位與下拉更新、直接掃描、重新建立 ViewModel 後讀取、儲存目前尚未更新的輸入、無效輸入不改檔案，以及刪除。

通知彈窗的 WPF 驗證涵蓋從主視窗開啟 modal 視窗、預設停用、URL 綁定、測試但不儲存、保存／重新載入、無效 URL 不覆寫、測試錯誤顯示與停用。使用模擬發送器及獨立暫存設定檔。

多道具監控的 WPF 驗證涵蓋多選、無效間隔、連續輪次、手動掃描互斥、下次時間、停止後不再掃描、設定重讀與重新啟動。所有驗證均不操作遊戲或發送正式頻道訊息。

## 未來整合位置與建議順序

1. 優先建立真實畫面資料集與辨識品質評估：取得可重複測試的截圖、確認欄位語意，處理置信度、無法辨識欄位、價格誤讀與裝備身分；低品質資料應避免觸發通知。
2. Infrastructure 新增實際 `IScreenCapture`（Windows 擷取）與 `IAuctionScreenAnalyzer`（OCR／Computer Vision）。Application 不依賴 Windows API、OpenCV、Tesseract 或畫面座標；正式擷取產生影像 frame，再於 `App.OnStartup` 替換注入的 adapter。
3. Infrastructure 實作 SQLite `IAuctionListingRepository`，建立 schema／migration、資料留存政策與 ID／市場分隔；Domain／Application 不引用 SQLite。
4. 實作 Windows `INotificationService`，加入重複通知抑制與重試政策。
5. 根據歷史樣本品質擴充低價策略與可比裝備政策，再加入跨輪通知抑制與跨執行個體同步。

尚無真實畫面時，最有價值的下一步是以手工 JSON 樣本擴充辨識錯誤／置信度模型，再加入 SQLite 保存與可比價格樣本品質控制。
