# Kingdom Rhapsody

## Unity 大廳

取得完整分支後，用 Unity Hub 開啟 [`unity/`](unity) 專案，版本為 **Unity 6.6（`6000.6.8f1`）**。在 Project 視窗開啟 `Assets/Scenes/Lobby.unity` 並按 Play。Game 視窗可選 9:16，或用 1000×1568 對照設計比例。

- 左右護衛各顯示一張目前的圖卡。點開後可在長槍兵和弩手之間切換，上方角色同步更新。
- 帶兵牌組有 8 個出戰格。先選出戰卡和收藏卡，再按「換上」。
- 按鈕壓下時縮放並變色，放開後回彈。點按、返回、確認和出戰各有 WAV 音效。右上角齒輪可以靜音。
- 護衛、牌組和音效設定會儲存在本機。滑鼠、觸控與方向鍵可操作，Enter 確認，Esc 關閉彈窗或返回大廳。

`PortraitLobby` 在 Play 時組合 uGUI Canvas。版面在 `Assets/Lobby/Scripts/PortraitLobby.cs`，圖層和 WAV 在 `Assets/Lobby/Resources/Lobby/`。卡牌縮圖會從同一個 repository 的 `art/` 匯入。更新精靈表後，可在 Unity 裡對對應的 `.lobbyportrait` 資產執行 Reimport。

「出戰」可透過 `PortraitLobby.BattleRequested` 把兩個護衛和 8 張出戰卡的 ID 傳給戰鬥流程。單獨開啟大廳場景時，按鈕會顯示出戰配置。

[UI 設計預覽與原始素材](art/ui)
