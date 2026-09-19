# Hướng dẫn: AI cục bộ tự tìm minimap (Windows)

## Trạng thái

Ứng dụng đã có **đường chạy mô hình AI ONNX thật** trên máy tính. Không gửi ảnh
màn hình game cho Gemini để xác định ranh giới bản đồ. Tuy nhiên **repository
KHÔNG kèm trọng số đã huấn luyện** và không quảng cáo thuật toán cũ là AI.
Bạn cần thu ảnh, gắn nhãn và tự huấn luyện (hoặc cài một mô hình tương thích)
trước khi nút **Tự tìm bằng AI trên máy** hoạt động.

Khi không có tệp mô hình, nút báo thiếu mô hình, không lấy khung ước lượng cũ để
giả làm kết quả AI. Bạn vẫn có thể mở **Ảnh bị lệch? Chỉnh tay nếu cần** và
**Xem trước lại** để sử dụng tạm. Ứng dụng vẫn yêu cầu bạn xem ảnh và bấm
**Dùng khung này** trước khi lưu khung và thu 5 ảnh.

## 1. Thu ảnh huấn luyện (chỉ khi bạn chủ động đồng ý)

1. Vào **Phòng Tập** trong Liên Minh; bảo đảm minimap nằm phía dưới bên phải.
2. Mở **Mắt nhìn AI → Thu ảnh → Chuẩn bị dữ liệu huấn luyện AI (tùy chọn)**.
3. Nhấn **Lưu 1 ảnh vùng dò để huấn luyện**, đọc kỹ cảnh báo và đồng ý.
4. Quay về cửa sổ trận, đợi 1–3 giây. Mỗi lần đồng ý chỉ lưu **một** ảnh ROI
   góc phải dưới (55% chiều rộng và 55% chiều cao cửa sổ game).
5. Lặp lại ở nhiều trận, giao diện, tỉ lệ minimap, độ phân giải và trạng thái
   minimap khác nhau. Nên có **hàng trăm ảnh đa dạng**, tối thiểu 30 ảnh hợp lệ
   chỉ để chạy thử quy trình; 30 ảnh không bảo đảm độ chính xác.

Ảnh PNG được lưu tại:

`%LOCALAPPDATA%\XerathSupportAssistant\minimap-ai-training\roi-*.png`

**Lưu ý quyền riêng tư:** ảnh ROI có thể chứa một phần địa hình game, HUD,
chữ hoặc nội dung bên ngoài minimap. Ảnh huấn luyện tồn tại trên ổ đĩa đến khi
bạn tự xóa. Không có thao tác tải ảnh lên mạng trong nút lưu ảnh; hãy xem
và xóa ảnh có nội dung không mong muốn trước khi gắn nhãn. Ảnh và mô hình
không được tự động đưa vào Git hoặc gửi Gemini.

## 2. Gắn nhãn vị trí minimap bằng chuột

Cài Python 3.11+ và thư viện dùng cho giao diện gắn nhãn:

```powershell
py -m pip install pillow
py tools/minimap_ai/label_roi.py
```

Trong cửa sổ gắn nhãn, kéo chuột khoanh **chính xác toàn bộ bản đồ nhỏ**,
không lấy thêm chân dung đồng minh, góc HUD khác hay vùng trống. Bấm **Lưu
khung đã chọn**, rồi sang ảnh tiếp. Nếu ảnh khó nhận ra minimap, **bỏ qua**
hoặc xóa ảnh; không bịa tọa độ. Mỗi ảnh hợp lệ sẽ có tệp nhãn YOLO cùng tên
`roi-xxx.txt`: một dòng `0 x_center y_center width height` chuẩn hóa
trong chính ảnh ROI. Không dùng nhãn vị trí tướng của thư viện V1.8:
đó là bài toán khác.

## 3. Huấn luyện và xuất ONNX

Chỉ sau khi có bộ ảnh gắn nhãn mới chạy:

```powershell
py -m pip install ultralytics onnx onnxruntime
py tools/minimap_ai/train.py --device cpu --epochs 60
```

Huấn luyện diễn ra trên máy; CPU có thể mất nhiều thời gian, có thể cấu hình
GPU bằng `--device 0` nếu môi trường PyTorch/CUDA tương thích. Lần đầu
Ultralytics có thể **tải trọng số YOLOv8n.pt từ Internet** nếu bạn chưa có
`--base-model` ở máy. Để hoàn toàn ngoại tuyến, cung cấp đường dẫn tệp
trọng số đã có. Kiểm tra giấy phép của Ultralytics trước khi phân phối mô
hình/sản phẩm thương mại.

Script từ chối tập chưa đủ 30 ảnh có nhãn; chia ngẫu nhiên khoảng 80/20 thành
tập học và kiểm tra nội bộ, xuất YOLOv8 **một lớp minimap, input
`[1,3,640,640]`, output `[1,5,N]`, không NMS**. Mỗi lần huấn luyện nên dùng
`--workspace` mới để tránh trộn lẫn tập dữ liệu. Mô hình sau huấn luyện được
chép tới:

`%LOCALAPPDATA%\XerathSupportAssistant\models\minimap-detector.onnx`

**Bắt buộc kiểm chứng riêng:** không coi thông số của tập kiểm tra ngẫu nhiên
là bằng chứng mô hình nhận diện đúng mọi trận. Hãy giữ lại ảnh của trận/ngày,
độ phân giải và giao diện khác hẳn để kiểm tra độc lập; ghi lại số ảnh
đúng toàn bộ minimap và số ảnh bị cắt mép/dính HUD. Đặc biệt thử bộ ảnh
minimap ở kích thước khác. Chưa có bộ ảnh được kiểm chứng và trọng số trong
repository nên hiện chưa thể nêu tỉ lệ chính xác.

## 4. Chạy thử trong ứng dụng

Đóng và mở lại Xerath Support Assistant sau khi cài ONNX (khởi động mới sẽ đọc
trạng thái mô hình). Vào trận thử, nhấn **Tự tìm bằng AI trên máy**, quay về
trận, đợi phần mềm lấy đúng **một ảnh RAM**. Trở lại ứng dụng xem khung.
Nếu sai, **không bấm Dùng khung này**; thử cải thiện dữ liệu rồi huấn luyện lại
hoặc chỉnh tay. Nếu đúng, chỉ sau khi bạn xác nhận, vùng cắt mới được lưu.

Tất cả thao tác xác định khung AI đều chạy cục bộ bằng ONNX CPU. Bộ phân tích
Gemini là chế độ riêng và **không tự bật**. Ứng dụng không tự điều khiển game.

## 5. Kiểm tra kỹ thuật & giới hạn

- Thử không có ONNX: nút báo thiếu mô hình, không tạo khung giả.
- Thử ONNX sai định dạng: hiển thị lỗi, không cho lưu khung sai.
- Thử AI dự đoán dưới ngưỡng 0,60: không xác nhận, chuyển sang chỉnh tay.
- Thử mô hình có kết quả: phải xem ảnh đúng *cùng một khung hình* đã chạy AI.
- Thử resize / thay UI scale: cần xem trước và xác nhận lại; mô hình có thể sai.
- Chỉ hỗ trợ minimap **góc dưới bên phải**, ảnh ROI như lúc gắn nhãn. Nếu
  chuyển minimap sang bên trái, không dùng mô hình hiện tại.
- ONNX Runtime trên máy hỗ trợ CPU; thời gian suy luận phụ thuộc phần cứng
  và kích thước ảnh, không cam kết tốc độ hoặc độ chính xác khi chưa đo thực tế.
