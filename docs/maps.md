# Bản đồ tự host

MapLibre chỉ tải sau khi khách bấm mở bản đồ ở checkout. Không WebGL, không JavaScript hoặc archive thiếu: khách vẫn nhập địa chỉ và chọn cơ sở bằng form/radio.

## Chuẩn bị dữ liệu

Tạo bản trích Việt Nam bằng CLI PMTiles chính thức theo Spike 005 trong repo docs. Archive cần schema Protomaps (layers `water`, `roads`, `places`) và PMTiles v3. Chọn mức zoom phù hợp phạm vi giao hàng, ghi nguồn/ngày/bounds/zoom và giữ giấy phép ODbL của dữ liệu OpenStreetMap. Bản thử hiện tại đến zoom 12 có giới hạn chi tiết đường/hẻm; chưa thay cho nghiệm thu địa chỉ thật.

Từ gốc backend:

```powershell
node scripts/maps-prepare.mjs C:/du-lieu/vietnam.pmtiles
```

Script kiểm header, chép archive vào `src/Bepthaymao.Api/maps/vietnam.pmtiles`, tải glyph Noto Sans Regular cho tiếng Việt từ kho Protomaps cùng giấy phép OFL. Không commit archive/glyph vào Git. Phải chạy trước khi khởi động API; `Maps__RootPath` cho phép trỏ tới thư mục đã chuẩn bị khi triển khai.

Nguồn: https://docs.protomaps.com/pmtiles/cli ; https://docs.protomaps.com/pmtiles/maplibre ; https://github.com/protomaps/basemaps-assets ; https://github.com/notofonts/noto-fonts . Attribution `© OpenStreetMap contributors` nằm trên bản đồ, dẫn tới https://www.openstreetmap.org/copyright .

## k3s

Manifest dùng PVC `maps` 2Gi, API mount chỉ đọc ở `/var/lib/bepthaymao/maps`. Operator cần nạp thư mục đã chuẩn bị vào PVC trước khi rollout; dữ liệu bản đồ không nằm trong image. Thiếu dữ liệu trả 404 và UI dùng đường dự phòng. Khi thay dữ liệu, rollout API để nạp file provider nếu trước đó thư mục chưa tồn tại. Không ghi đè archive đang phục vụ từng phần: chuẩn bị bản mới rồi thay atomically/rollout.

Sau triển khai, kiểm qua ingress thật (chưa được tự tính pass chỉ vì manifest hợp lệ):

```powershell
curl.exe -D - -o head.bin -H "Range: bytes=0-126" https://ten-mien/maps/vietnam.pmtiles
```

Cần `206`, `Content-Range: bytes 0-126/<size>`, đúng 127 byte; thử ngoài tệp nhận `416`. Kiểm nhãn tiếng Việt, không gọi tile OSM công cộng và chất lượng ghim tại địa chỉ giao hàng thật. Rollback: giữ archive/style tương thích schema, đổi lại dữ liệu và rollout; form/radio vẫn là fallback.
