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

在同一台電腦開啟兩個 Unity 客戶端，從大廳進入「出戰」→「雙人對戰」，兩邊都填 `http://127.0.0.1:18120`。其中一端可用 Unity Editor，另一端使用相同提交的獨立 Player。用 `/healthz` 檢查 Go 行程。

用同一個區域網路上的兩台電腦測試時，Windows 主機可從倉庫根目錄執行 `.\server\run.ps1 -http 0.0.0.0:18120`。手動啟動時則在 `server/` 執行 `go run . -http 0.0.0.0:18120`。兩邊遊戲都填主機的區域網路網址。

WebGL 成品會包含 `StreamingAssets/KingdomRhapsody/transport.js`，由遊戲頁面同源載入。可用 `-web /path/to/web-build` 同源提供 Unity 成品和 WebSocket。若將 Web 成品與伺服器放在不同網域，用 `-origins https://web.example.com` 授權遊戲頁面的來源，並由 HTTPS 反向代理轉發 `/ws`。本機預設只監聽迴圈位址。

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
