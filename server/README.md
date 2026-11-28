# Kingdom Rhapsody 1v1 伺服器

需要 Go 1.23 以上、可編譯 `net8.0` 的 .NET SDK，以及執行工作程序所需的 .NET 8 Runtime。

在 Windows PowerShell 從倉庫根目錄執行。

```powershell
.\server\run.ps1
```

腳本會先編譯 Release 版 `battle-worker`，成功後啟動 Go 服務。保留執行中的 PowerShell 視窗，按 Ctrl+C 停止服務。

在 Linux 或 macOS，從倉庫根目錄執行。

```sh
dotnet build -c Release server/battle-worker/BattleWorker.csproj
cd server
go run .
```

Unity Editor 和桌面 Player 預設使用 `http://127.0.0.1:18120`。在同一台電腦開啟兩個同版本客戶端，從大廳進入「出戰」→「雙人對戰」即可配對。用 `/healthz` 檢查 Go 服務。

WebGL 建置使用滿版網頁範本，畫布會隨瀏覽器視窗縮放。寬螢幕視窗可點右下角的「全螢幕」進入瀏覽器全螢幕。

WebGL 預設連遊戲頁面的同一個來源，傳輸模組由成品的 `StreamingAssets/KingdomRhapsody/transport.js` 載入。用 `-web` 指定含 `index.html` 的成品資料夾，Go 即可同源提供遊戲與 WebSocket。下例從倉庫根目錄啟動。

```powershell
$webBuild = (Resolve-Path .\unity\Builds\WebGL).Path
.\server\run.ps1 -web $webBuild
```

兩個瀏覽器視窗都開啟 `http://127.0.0.1:18120/`，再直接選「雙人對戰」。在同一個區網測試時，主機可加上 `-http 0.0.0.0:18120`，兩台電腦都用主機的區網網址開啟瀏覽器版。

需要固定連到其他主機時，在 Unity 開啟 `Assets/Scenes/Lobby.unity`，於 `PortraitLobby` 元件的 **Battle Server Url** 設定伺服器網址後建置。Web 與伺服器分開部署時，Go 服務加上 `-origins https://web.example.com` 授權遊戲頁面來源，並由 HTTPS 反向代理轉發 `/ws`。

線上對戰目前使用協定第 2 版。更新時重新建置 Unity 客戶端及 `battle-worker`，並重啟 Go 服務。

Go 服務使用 GameNetKit 的 Binary WebSocket 邊界處理，依加入順序配置雙方席次，轉交命令給每場專用的 .NET 戰鬥程序。`battle-worker` 從 Unity 專案編譯同一套 `BattleMatch` 程式碼，讀取相同的動畫命中設定。客戶端只送部署和疾行請求。對戰的身份、能量、部署、傷害和勝負由伺服器判定。比賽中斷線按棄權處理。伺服器送完終局狀態和命令回覆後會收線，玩家可以從結算畫面重新配對。可用 `-rooms` 調整最多同時進行的對戰數，預設 12。

## 驗證兩個連線

先完成上述 Release build，再從 `server/` 執行。

Linux、macOS

```sh
KR_TEST_DOTNET="$(command -v dotnet)" KR_TEST_WORKER="$PWD/battle-worker/bin/Release/net8.0/BattleWorker.dll" go test -race -v ./...
```

Windows PowerShell

```powershell
$env:KR_TEST_DOTNET = (Get-Command dotnet).Source
$env:KR_TEST_WORKER = (Resolve-Path .\battle-worker\bin\Release\net8.0\BattleWorker.dll).Path
go test -v ./...
```

整合測試使用兩個獨立的 WebSocket 連線和實際戰鬥程序，驗證同源與授權跨來源配對、私有手牌、席次與左右護衛、能量和指令序號、相同 tick 的傷害同步，以及斷線結算。另一項測試讓兩端留在終局，確認最後的特效與回覆都已送達，連續六場均可回收房間及連線名額。

Unity 傳輸邊界在 `unity/Packages/com.realtime.transport/`，瀏覽器模組在 `unity/Assets/StreamingAssets/KingdomRhapsody/`，Go 傳輸邊界在 `server/internal/gamenetkit/transport/binary.go`。上游版本見 [GameNetKit](https://github.com/win30221/gamenetkit/tree/8e923249f68d71a343a3224200494603ed8712ea)。
