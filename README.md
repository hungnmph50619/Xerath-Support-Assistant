# Xerath Support Assistant · Companion V0.5 — Wave & Fight Advisor

**Mới trong V0.5:** nút **Phân tích thế lính & giao tranh** trên cửa sổ Companion mở công cụ **Wave & Fight Advisor** để bạn tự nhập tình huống đã quan sát từ ảnh/video trận đấu. Công cụ trả về ba nhận định bằng tiếng Việt, mỗi nhận định có lời giải thích, dữ kiện làm căn cứ và danh sách thông tin còn thiếu: **có nên đẩy lính**, **có nên dâng cao**, **điều kiện giao tranh thuận lợi hoặc bất lợi**.

### Hướng dẫn nhanh
1. Đóng trợ lý đang chạy, mở PowerShell tại thư mục repo Windows, chạy `git pull origin main` rồi `.\RUN_WINDOWS.cmd`.
2. Nhấn **Phân tích thế lính & giao tranh**. Để xem thử, nhấn **Điền ví dụ** → **Phân tích tình huống**. Ví dụ cố ý để khu vực sông thiếu tầm nhìn: kết quả phải cảnh báo không nên tùy tiện dâng cao.
3. Với tình huống khác, chọn dữ kiện đã quan sát: vị trí và hướng lính, ý định ADC, máu/năng lượng, tầm nhìn, lần cuối biết rừng địch, vị trí ADC, tương quan người, kỹ năng mở giao tranh, lính hai bên và thời điểm mục tiêu.
4. Để mục `Chưa rõ` khi không biết. Các nhận định phụ thuộc dữ liệu thiếu sẽ hiển thị **chưa đủ thông tin** thay vì bịa ra kết luận. Nhấn **Xóa dữ liệu** để phân tích tình huống tiếp theo.

**Giới hạn quan trọng:** V0.5 là công cụ **xem lại tình huống độc lập với trận đang diễn ra**. Không đọc màn hình hoặc API trận, không dự đoán vị trí rừng ngoài tầm nhìn, không đưa ra khuyến nghị tự động dựa trên game trực tiếp, không mở overlay, không tự điều khiển nhân vật, không phát giọng AI cho nhận định động. Bộ hẹn giờ và giọng nói trong Companion vẫn hoạt động riêng như V0.3. Nhận định chỉ là quy tắc có điều kiện để học từ tình huống, không cam kết giao tranh thắng lợi. Trong các trường hợp tầm nhìn thiếu, rừng mất dấu hoặc ADC định giữ lính, các quy tắc tránh đề xuất đẩy cao/mở giao tranh một cách quả quyết.

**Mã nguồn:** `src/XerathAssistant.Core/WaveFightAdvisor.cs`, `src/XerathAssistant.Desktop/WaveFightAdvisorWindow.xaml` và `.xaml.cs`. Bộ kiểm thử trong `tests/XerathAssistant.CoreTests/Program.cs` bổ sung các trường hợp thiếu dữ liệu, giữ lính, thiếu tầm nhìn, thiếu người và mục tiêu sắp xuất hiện. Chưa xác nhận chương trình biên dịch/chạy trên máy Windows của bạn; có thể kiểm tra bằng `dotnet build src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj` và `dotnet run --project tests/XerathAssistant.CoreTests/XerathAssistant.CoreTests.csproj`.

---

## V0.4 — Last Seen Review

**Mới trong V0.4:** nút **Xem lại vị trí rừng địch** mở cửa sổ riêng để chọn ảnh trận đã lưu và tự bấm đánh dấu vị trí rừng đối phương trên minimap của ảnh. Nhập mốc thời gian game ở ảnh (mm:ss), ghi chú, tùy chọn mốc xem lại, và xóa/thay dấu khi có ảnh mới. Đây là dấu trong **ảnh đã lưu**, không phải vị trí thực tế hiện tại, không tự nhận diện từ live game và không overlay lên trận. Mô-đun sử dụng `LastSeenReview.cs` cùng các bài kiểm thử dữ liệu và thời gian.

**Cập nhật trên Windows:** đóng trợ lý đang chạy, mở PowerShell tại thư mục dự án, chạy `git pull origin main`, rồi `.\\RUN_WINDOWS.cmd` (hoặc `dotnet run --project src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj`). Trong cửa sổ Companion, chọn **Xem lại vị trí rừng địch**, mở ảnh đã lưu bằng nút tương ứng rồi tự bấm vào vị trí trên minimap trong ảnh. Nếu muốn xem lại sau trận, bạn không cần mở Liên Minh. Tính năng xem lại không ghi màn hình hay ảnh vào GitHub.

**Phạm vi:** Bản này chưa tự cảnh báo giao tranh Top/Mid, vị trí rừng địch hay xuống gank trong trận; phần hỗ trợ trực tiếp phải được xem xét riêng về cách lấy dữ liệu và chính sách Riot. Các lời nhắc hiện tại vẫn chạy theo đồng hồ.

---

## V0.3 — Vietnamese AI voice

Ứng dụng C#/.NET 8/WPF chạy cùng lúc với Liên Minh Huyền Thoại và nhắc những công việc của Xerath SP. Phiên bản này thay bộ đọc System.Speech tiếng Anh bằng **giọng AI Ban Mai — nữ miền Bắc** của FPT.AI. Âm lượng phát mặc định 70%.

## Cập nhật trên Windows

Trong PowerShell, mở thư mục dự án đã clone và chạy:

```powershell
git pull origin main
.\RUN_WINDOWS.cmd
```

Cần Windows 10/11, .NET 8 SDK. Mở Liên Minh và vào trận, sau đó mở trợ lý.

## Tạo giọng tiếng Việt lần đầu (cần mạng và API key riêng)

1. Tạo tài khoản tại https://console.fpt.ai/ và bật **Text to Speech** trong dự án, tạo API key có quyền sử dụng TTS. Có thể có hạn mức hoặc chi phí dịch vụ tùy tài khoản; xem bảng giá tại nhà cung cấp.
2. Trong **Xerath Support Assistant**, chọn các mục nhắc muốn dùng. Dán API key vào ô mật khẩu trong ứng dụng **trên máy của bạn**. Không gửi key qua ChatGPT, không commit lên GitHub.
3. Nhấn **Tạo và lưu giọng tiếng Việt**. Đợi đến khi thông báo đã lưu đủ số lời nhắc (FPT.AI xử lý bất đồng bộ nên có thể mất vài phút). Nhấn **Nghe thử**.
4. Nhấn **Bắt đầu nhắc** rồi quay lại game. Các bản MP3 đã tạo được lưu trong `%LOCALAPPDATA%\XerathSupportAssistant\voice\fpt-banmai-v1`. Những lần sau, không cần mạng hoặc API key để phát các câu đã lưu. Nếu bật thêm mục nhắc chưa có âm thanh, hãy nhập key và tạo phần còn thiếu.

Trợ lý **không dùng giọng Windows/tiếng Anh làm dự phòng**. Nếu chưa tạo đủ âm thanh cho các mục đã chọn, nút Bắt đầu sẽ yêu cầu chuẩn bị trước. Có thể bỏ chọn giọng nói để chỉ dùng thông báo chữ. Key chỉ giữ trong ô nhập tới lúc chuẩn bị xong rồi được xóa; ứng dụng không ghi key vào file cấu hình hoặc repository.

Nhà cung cấp: FPT.AI TTS v5, API `https://api.fpt.ai/hmi/tts/v5`, giọng `banmai`; mã nguồn trong `src/XerathAssistant.Desktop/FptVietnameseVoiceService.cs`. Văn bản lời nhắc được gửi tới FPT.AI để tổng hợp âm thanh. Nếu không muốn gửi văn bản ra dịch vụ bên ngoài, tắt tính năng giọng nói.

## Chức năng hiện tại

- Giao diện Companion phát hiện **tiến trình Windows** của League Client và tiến trình trận (không đọc trạng thái hoặc nội dung game).
- Lịch nhắc độc lập: minimap 45 giây; tầm nhìn 3 phút; rừng đồng minh 90 giây; ADC 2 phút; năng lượng/vị trí Xerath 2 phút 30 giây; mục tiêu lớn 3 phút. Gộp lời nhắc đồng thời, chống nhắc lặp tối thiểu 30 giây.
- Giọng AI tiếng Việt đã lưu MP3, phát lần lượt nếu nhiều mục đến hạn cùng lúc; tạm dừng/kết thúc dừng âm thanh. Aim Lab V0.1 mở riêng qua nút **Mở Aim Lab V0.1**.

**Chưa có** nhận diện gank Near/Arrived, tự đọc minimap, dự đoán Q/W/E/R trong trận hay điều khiển nhân vật. Các lời nhắc rừng đồng minh hiện là lời nhắc kiểm tra minimap theo giờ, không phải cảnh báo gank thực tế. Không can thiệp Vanguard.

## Biên dịch và kiểm thử

```powershell
dotnet build src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj
dotnet run --project tests/XerathAssistant.CoreTests/XerathAssistant.CoreTests.csproj
dotnet run --project src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj
```

Workflow tự động: `.github/workflows/windows-build.yml`. Sau khi cập nhật, nếu có lỗi build hoặc phát âm thanh, chụp thông báo PowerShell/cửa sổ ứng dụng để xử lý. Bản cập nhật chưa được xác nhận chạy trên máy Windows của bạn.

Tham khảo: https://docs.fpt.ai/docs/vi/speech/api/text-to-speech/
