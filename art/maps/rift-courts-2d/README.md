# 斷崖雙庭 2D 場景

《王國狂想》的直式戰場底圖。紅方在上，藍方在下。兩座石庭以三座橋相連，銅色格柵標示中路陷阱。金邊旗幟、獅紋和石材採用王子參考圖的厚塗風格。

## 檔案

| 檔案 | 用途 |
| --- | --- |
| [RiftCourts_2D.png](RiftCourts_2D.png) | 1056 × 1440 無損底圖，1,906,779 位元組 |
| [RiftCourts_2D.webp](RiftCourts_2D.webp) | 同尺寸 WebP 壓縮底圖，179,874 位元組 |
| [RiftCourts_2D_Layout.json](RiftCourts_2D_Layout.json) | 地圖邊界、站位、通道、部署區及相機 |
| [RiftCourts_2D_Source.png](source/RiftCourts_2D_Source.png) | 1122 × 1402 原始繪圖 |
| [RiftCourts_Paint_Guide.png](source/RiftCourts_Paint_Guide.png) | 生成時使用的構圖參考 |
| [RiftCourts_2D_Mobile_Preview.png](preview/RiftCourts_2D_Mobile_Preview.png) | 三種手機比例的固定高度裁切對照 |
| [PROMPT.md](PROMPT.md) | 實際提示詞和兩張參考圖的用途 |

## 對位

2D 座標在 XY 平面，X 往右、Y 往上。圖片中心對準世界原點，30 像素等於 1 單位，底圖覆蓋 35.2 × 48 單位。角色腳底對準 JSON 內的站位，角色、隊伍提示及特效擺在底圖上層。兩隊在玩法座標上以原點作 180° 對稱。通道與站位依這張 2D 圖的實際可走範圍校準。

正交視野中心為 `(0, 0)`，半高 24 單位。將高度固定為 48 單位，可視寬度用 `48 × 遊戲視窗寬度 ÷ 遊戲視窗高度` 計算。

| 直式視窗比例 | 可視寬度 |
| --- | ---: |
| 9:21 | 20.57 單位 |
| 9:19.5 | 22.15 單位 |
| 9:16 | 27.00 單位 |

中央陷阱的位置和週期已記在 JSON 裡。安全 3 秒、預警 1 秒、攻擊 2 秒，對地面士兵生效，王子與飛行單位免疫。遊戲端仍需接入碰撞、移動及陷阱邏輯。
