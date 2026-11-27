# Kingdom Rhapsody

## Unity 大廳

取得完整分支後，用 Unity Hub 開啟 [`unity/`](unity) 專案，版本為 **Unity 6.6（`6000.6.8f1`）**。在 Project 視窗開啟 `Assets/Scenes/Lobby.unity` 並按 Play。Game 視窗可選 9:16，或用 1000×1568 對照設計比例。

- 左右護衛各顯示一張目前的圖卡。點開後可在長槍兵和弩手之間切換，上方角色同步更新。
- 帶兵牌組固定 8 個出戰格，上陣區與收藏區共用四欄等尺寸卡牌；收藏超過可視高度時可垂直捲動，出戰區固定。點選卡牌後，在卡牌附近浮出「看資訊」；未上陣的收藏卡另外顯示「上陣」。點「上陣」後，出戰卡牌亮框並微幅晃動，點任何出戰格即交換及儲存，不需二次確認。點「取消」或 Esc 結束交換。
- 收藏捲動展示：Unity Editor 與 Development Build 預設額外顯示 21 張帶 `DEMO` 標記的測試卡（現有 3 張收藏卡加上測試卡，共 24 張、6 排）。測試卡沿用現有兵種圖像、不可上陣，也不會進入存檔或正式發行版。可在 `PortraitLobby` Inspector 的 `Collection Demo Count` 設為 0 關閉，或調整 0–100，重新進入 Play 即生效。
- 兵種資訊目前顯示完整立繪、上陣狀態與素材清單中的代表動作；正式戰鬥數值及 Idle 動畫尚未接入。
- 目前隨附字型為舊版中文字型子集。Editor/桌面優先使用現有字型，缺字時嘗試已安裝的繁體中文字型。發行前請執行 `python -m pip install fonttools` 與 `python tools/Prepare-LobbyFont.py`，生成額外的 `KingdomUiTCFull.otf`；重新進入 Play 後即優先使用適合的字型。原字型及授權保留。
- 按鈕壓下時縮放並變色，放開後回彈。點按、返回、確認和出戰各有 WAV 音效。右上角齒輪可以靜音。
- 護衛、牌組和音效設定會儲存在本機。滑鼠、觸控與方向鍵可操作，Enter 確認，Esc 關閉彈窗或返回大廳。

`PortraitLobby` 在 Play 時組合 uGUI Canvas。版面在 `Assets/Lobby/Scripts/PortraitLobby.cs`，圖層和 WAV 在 `Assets/Lobby/Resources/Lobby/`。卡牌縮圖會從同一個 repository 的 `art/` 匯入。更新精靈表後，可在 Unity 裡對對應的 `.lobbyportrait` 資產執行 Reimport。

「出戰」可透過 `PortraitLobby.BattleRequested` 把兩個護衛和 8 張出戰卡的 ID 傳給戰鬥流程。單獨開啟大廳場景時，按鈕會顯示出戰配置。

[UI 設計預覽與原始素材](art/ui)
