# 角色底稿與提示詞紀錄

目前可核對的資料包括角色參考圖、六組製作底稿及動畫渲染規格。當時送出的逐字提示詞無法從現有紀錄復原。下列區塊是依底稿和已確認的美術要求重建的提示詞。

共同美術要求為硬派奇幻、斜俯視角、細緻 2D 精靈圖搭配 3D 場景。角色在不同角度維持比例、光源與裝備位置。王子的藍金獅徽、厚白毛領與金王冠取自使用者提供的八向參考圖。

## 王子八向軀幹

使用底稿 `皇家藍金披風軀幹精靈圖.png`。走路與衝刺共用此底稿，並從參考圖擷取入鞘佩劍。

```text
Use the supplied eight-direction prince turnaround as the character reference. Paint a clean transparent game sprite sheet of the same short, broad-shouldered hard-fantasy prince from eight directions. Keep his gold crown, brown hair and beard, heavy dark armor, thick white fur collar, and deep-blue cape with gold trim and a golden lion on the back. Use detailed, smoothly shaded 2.5D character art under a consistent soft overhead light and a fixed slightly elevated orthographic camera. Show the head, neck, torso, belt and cape as a waist-up base for separate animation parts. Keep the silhouette, scale, and costume details consistent. Arrange equal cells in a 4 × 2 grid. The top row is N, NW, NE, E. The bottom row is W, SE, S, SW. N faces away and S faces the camera.
```

## 王子護甲零件

使用底稿 `王子裝甲四肢圖集.png`。王子兩個移動動作都從同一份圖集擷取護甲。

```text
Create a transparent parts atlas for the reference prince. Paint each part as a separate cutout with the same softly shaded brown armor, worn gold trim, quilted fabric and leather as the torso. Use five columns for segmented upper-arm armor, forearm armor with a closed gauntlet, armored thigh with a gold knee guard, shin armor, and boot. Draw the front-facing parts on the first row and the profile parts on the second row. Keep parts separated by transparent space and show their complete contours for rotation at the joints.
```

## 長槍八向軀幹

使用底稿 `藍羽王室衛兵八向精靈圖.png`。長槍走路與突刺使用相同的頭盔、盔甲和披風。長矛與盾在動畫渲染時合成。

```text
Paint an eight-direction torso atlas of an original royal spearman in the same hard-fantasy 2.5D style as the gold-crowned prince. Give the guard a closed steel visor, a large swept royal-blue plume, heavy silver plate with worn gold borders, and a deep-blue cape. Maintain the same armor and head proportions in every angle. Show the head, shoulder plates, torso, waist and cloak as a base for articulated armor and separate weapon layers. Use a fixed slightly elevated orthographic camera and consistent overhead lighting. Arrange the eight transparent cells as N, NW, NE, E on top and W, SE, S, SW below.
```

## 弓手八向軀幹

使用底稿 `藍圍巾皇家弩兵精靈圖.png`。弓手走路與射擊共用此底稿，弩身由渲染程式按雙手的位置合成。

```text
Paint an eight-direction torso atlas of an original royal archer from the same hard-fantasy royal army. Give the archer a pointed silver helmet with open face, a deep-blue scarf, a gold-edged short blue cape, and a brown back quiver with bolts. Use the reference guard proportions, softly shaded steel and worn gold trim, and the same slightly elevated orthographic camera. Keep the scarf, quiver strap and quiver on the correct anatomical sides in every view. Show a waist-up base for separate arms and a handheld crossbow. Arrange eight equal transparent cells as N, NW, NE, E on top and W, SE, S, SW below.
```

## 護衛共用護甲零件

使用底稿 `皇家衛兵裝甲零件圖集.png`。長槍與弓手各動作共用此圖集，手套和前臂分開繪製。

```text
Create a transparent 5-column by 2-row cutout sheet of the royal guards' armor parts. Match the guards' shaded steel, bronze-gold edges, dark leather and slightly worn finish. Show isolated shoulder and upper-arm armor, forearm armor with a closed glove, overlapping thigh plates, shin guard, and boot. Draw the front view on the first row and the side view on the second row. Keep pieces at the same scale with ample transparency between their contours. Make the cuff and glove distinct so the animator can separate them and attach hands to their grips.
```

## 護衛背面與背斜角

使用底稿 `六枚背面深藍斗篷兵棋.png`。兩位護衛的 N、NW、NE 角度使用此底稿。

```text
Paint six consistent rear torso sprites of the existing royal guards. Use a 3-column by 2-row transparent grid. The first row is the crested spearman. The second row is the scarfed archer with the quiver visible against the cape. In each row show N, NW and NE in that order. Show the deep-blue cloak from the back, its gold edge and the correct helmet silhouette. Match the head-to-waist proportions, camera elevation and light direction of the eight-direction front and side torso sheets.
```

## 最終動畫產製紀錄

六組成品由原畫切片、關節姿勢、八向投影及遮擋順序逐幀渲染。每個方向渲染 12 幀。

| 精靈表 | 動作與裝備 |
| --- | --- |
| `Prince_Walk` | 12 FPS 的原地走路循環，腳跟落地、腳掌承重、腳尖推離地面，佩劍入鞘。 |
| `Prince_Sprint` | 20 FPS 的原地衝刺循環，身體前傾、步幅拉長、披風後飄，佩劍入鞘。 |
| `Spearman_Walk` | 12 FPS 原地走路，持盾與持矛隨踏步小幅反向擺動。 |
| `Spearman_Attack` | 16 FPS 單次突刺，依序蓄力、出矛和收勢，第 7 幀到達突刺點。 |
| `Archer_Walk` | 12 FPS 原地走路，雙手持弩，角色右手在扳機、左手托弩。 |
| `Archer_Attack` | 16 FPS 單次射擊，抬弩、瞄準、放箭後收勢，第 7 幀放箭。 |
