VRCPhotoRotationCorrector
====

VRChatのカメラ機能で撮影した画像の向きを一括で補正します。

向きの検出には[はこつきさんの成果](https://github.com/rehakomoon/VRC_PhotoRotation_Estimation)を利用しています。

## 使い方
1. `VRCPhotoRotationCorrector.exe` を起動します
2. Folder path の欄に画像ファイルが保存されているフォルダを設定します
3. Correct ボタンを押すとすべての画像に対して向きの検出と補正を行います

## 注意事項
- 補正処理は結構重たいです。CPU全コアをそれなりの時間占有するので画像の枚数には気をつけてください。
- まだエラー処理がかなり甘いです。安全を期すなら元ファイルをどこかにコピーしてそれに対して補正をかける方が良いです。
