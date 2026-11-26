# 《王國狂想》新增兵種與特效

十一位兵種各有八向走路和一套代表動作。每個方向由左到右排列 12 幀，上到下的方向為 **N、NE、E、SE、S、SW、W、NW**。
走路採 12 FPS 原地循環。其他動作採 16 FPS 單次播放，主事件在第 7 幀。冰霜預警圈可在第 3 幀開始。
切格採 Offset 0、Padding 0、100 PPU 及左下原點 Pivot `(0.5, 0.2)`。輕騎兵使用較寬的 320 px 單格容納人馬和騎槍，其餘是 256 px。

| 兵種 | 走路 | 代表動作 | 單格 |
| --- | --- | --- | --- |
| 盾兵 | [走路 PNG](shieldman/Shieldman_Walk.png) | [盾擊 PNG](shieldman/Shieldman_Bash.png) | 256 × 256 |
| 新手弓手 | [走路 PNG](bow-recruit/BowRecruit_Walk.png) | [射箭 PNG](bow-recruit/BowRecruit_Shoot.png) | 256 × 256 |
| 火炮槍兵 | [走路 PNG](cannoneer/Cannoneer_Walk.png) | [開炮 PNG](cannoneer/Cannoneer_Fire.png) | 256 × 256 |
| 近戰小兵 | [走路 PNG](footman/Footman_Walk.png) | [短劍揮砍 PNG](footman/Footman_Slash.png) | 256 × 256 |
| 皇家軍旗手 | [走路 PNG](standard-bearer/StandardBearer_Walk.png) | [舉旗鼓舞 PNG](standard-bearer/StandardBearer_Rally.png) | 256 × 256 |
| 戰地醫官 | [走路 PNG](field-medic/FieldMedic_Walk.png) | [治療 PNG](field-medic/FieldMedic_Heal.png) | 256 × 256 |
| 皇家輕騎兵 | [走路 PNG](light-cavalry/LightCavalry_Walk.png) | [衝鋒 PNG](light-cavalry/LightCavalry_Charge.png) | 320 × 320 |
| 重鎚兵 | [走路 PNG](hammer-guard/HammerGuard_Walk.png) | [砸地震波 PNG](hammer-guard/HammerGuard_Slam.png) | 256 × 256 |
| 巨斧兵 | [走路 PNG](axe-guard/AxeGuard_Walk.png) | [橫掃 PNG](axe-guard/AxeGuard_Sweep.png) | 256 × 256 |
| 冰霜術士 | [走路 PNG](frost-mage/FrostMage_Walk.png) | [召喚冰晶 PNG](frost-mage/FrostMage_Cast.png) | 256 × 256 |
| 火焰術士 | [走路 PNG](flame-mage/FlameMage_Walk.png) | [直線火浪 PNG](flame-mage/FlameMage_Cast.png) | 256 × 256 |

## 特效

箭矢和炮彈是獨立透明圖。箭矢和火浪以向右為初始方向。箭矢可在放箭幀生成，命中時另播火花。火炮在開炮幀同時播炮口閃光，炮彈抵達落點後再播爆炸。
各動畫型特效從左到右排列。地面效果可依關卡的實際判定範圍調整尺寸。飛行、碰撞、傷害、減速和治療數值由遊戲邏輯提供。

| 素材 | 單格 | 影格 | 建議幀率 |
| --- | --- | --- | --- |
| [箭矢](../effects/Arrow.png) | 96 × 32 | 1 | 單張 |
| [炮彈](../effects/Cannonball.png) | 48 × 48 | 1 | 單張 |
| [箭矢命中火花](../effects/ArrowImpact.png) | 64 × 64 | 8 | 20 FPS |
| [砲口閃光](../effects/CannonFlash.png) | 96 × 96 | 6 | 24 FPS |
| [炮彈落點爆炸](../effects/CannonExplosion.png) | 128 × 128 | 10 | 20 FPS |
| [冰霜預警圈](../effects/FrostWarning.png) | 128 × 128 | 8 | 16 FPS |
| [冰晶爆發](../effects/FrostBurst.png) | 128 × 128 | 10 | 20 FPS |
| [結霜地面](../effects/FrostGround.png) | 128 × 128 | 10 | 10 FPS |
| [直線火浪](../effects/FlameWave.png) | 128 × 96 | 10 | 20 FPS |
| [短劍揮砍弧光](../effects/SwordArc.png) | 96 × 96 | 8 | 24 FPS |
| [巨斧橫掃弧光](../effects/AxeArc.png) | 128 × 128 | 8 | 20 FPS |
| [盾擊火花](../effects/ShieldImpact.png) | 96 × 96 | 8 | 20 FPS |
| [重鎚震波](../effects/HammerWave.png) | 128 × 128 | 10 | 20 FPS |
| [鼓舞光圈](../effects/RallyAura.png) | 128 × 128 | 10 | 16 FPS |
| [治療光效](../effects/HealPulse.png) | 128 × 128 | 10 | 16 FPS |
| [衝鋒塵土](../effects/ChargeDust.png) | 96 × 96 | 8 | 16 FPS |

[動畫資料與出手對齊點](expanded-roster.json)包含專案根目錄相對路徑、播放順序、從零起算的事件幀，以及每個方向在動作影格內由左上起算的像素座標。
