# Xerath Support Assistant · V1.5 — Phân tích ảnh minimap trong RAM, không lưu ảnh mới

**Đã thay công cụ "Thu ảnh minimap" bằng chế độ phân tích theo phiên chỉ dùng bộ nhớ tạm.** Không còn tạo thư mục ảnh/tệp JPG mới trong phiên: mỗi lần chỉ cắt vùng minimap của cửa sổ game đang được chọn → giữ ảnh JPEG trong RAM → gửi đến API AI Cá Nhân tại `127.0.0.1:5188` → AI Cá Nhân chuyển đến **Google Gemini** để nhận xét hình ảnh → giải phóng ảnh trong RAM. Không lưu video, không ghi ảnh ra ổ đĩa và không tự đưa kết quả lên HUD hoặc đọc vị trí địch thành lời trong trận. Để chạy chế độ này bạn vẫn cần AI Cá Nhân ở nhánh `feature/v2.3.0-minimap-vision-lab`, máy chủ trên cổng 5188 và Gemini đã cấu hình, hỗ trợ nhận ảnh.

**Quyền riêng tư và chi phí:** bạn phải bấm **Bắt đầu phân tích** và xác nhận riêng **cho cả phiên** rằng tối đa 45 ảnh minimap sẽ được gửi qua AI Cá Nhân tới Gemini. Mặc định tối đa một ảnh mỗi 60 giây; có thể chọn 30 hoặc 120 giây trước khi bật. Chỉ duy trì một yêu cầu phân tích tại một thời điểm, không dồn hàng đợi. Gemini có thể áp dụng hạn mức, phí và chính sách lưu trữ riêng; dừng/xóa ảnh trên máy **không đồng nghĩa xóa ảnh đã gửi khỏi nhà cung cấp bên ngoài**. Nếu không muốn gửi ảnh tới Gemini, đừng bật chế độ này. Hiện chưa tích hợp mô hình thị giác chạy hoàn toàn trên máy.

**Chỉ đọc khi có trận:** cần phát hiện tiến trình `League of Legends` trước khi bắt đầu; chỉ chụp nếu cửa sổ game được chọn và phần minimap nằm trong vùng hiển thị. Khi tắt game, công cụ tự dừng ở nhịp kiểm tra tiếp theo. Nếu chuyển sang cửa sổ khác, công cụ ngừng chụp và tự dừng nếu không quay lại game sau ba phút; cũng tự dừng khi đạt 45 ảnh/phiên hoặc khi bạn đóng cửa sổ/bấm **Dừng**. Kết quả văn bản phiên gần nhất cũng được xóa khỏi giao diện khi dừng. Cửa sổ chỉ dùng cho luyện tập hoặc xem lại: nhận xét từ Gemini chưa được xác minh và không phải công cụ tự theo dõi rừng/mid trong trận.

**Xóa ảnh cũ:** ảnh được tạo bởi **V1.3/V1.4** vẫn còn trên ổ đĩa và sẽ không tự biến mất sau khi nâng cấp. Để xóa, mở công cụ mới và bấm **Xóa ảnh minimap ĐÃ LƯU từ các phiên cũ**, đọc xác nhận rồi đồng ý. Chương trình chỉ xóa các tệp `minimap_*.jpg` trong thư mục phiên trực tiếp dưới `%LOCALAPPDATA%\XerathSupportAssistant\minimap-training`, không xóa ảnh của bạn ở nơi khác hoặc tệp tên khác. Không thể khôi phục các tệp đó bằng nút này.

**Cài đặt:** đóng Xerath Support Assistant đang chạy rồi trong PowerShell chuyển tới thư mục clone Xerath, chạy `git pull origin main` và `.\RUN_WINDOWS.cmd`. Trong Companion V1.5 bấm **Phân tích minimap trực tiếp trong RAM (cần đồng ý gửi Gemini)**, chọn chu kỳ, bắt đầu trận luyện tập, bấm **Bắt đầu phân tích**, đọc xác nhận và quay lại game ở chế độ cửa sổ/không viền. Nếu AI Cá Nhân chưa mở, trả lỗi, không nhận ảnh, hoặc mô hình/hạn mức Gemini không sẵn sàng thì công cụ dừng và báo lỗi; không ghi ảnh dự phòng. Cần thử thực tế trên máy người dùng để xác nhận vùng cắt và kết quả phân tích đúng độ phân giải. Quy trình CI chỉ kiểm chứng build và kiểm thử mã, không kiểm chứng được nhận diện đúng Rừng/Mid thực tế.

---

## V1.4 — Giọng xem lại ảnh cũ (tài liệu phiên bản trước)

**Đã triển khai bản thử nghiệm đọc tiếng Việt cho hai vai trò Rừng địch và Mid đối phương, dựa trên vị trí BẠN đánh dấu trên ảnh đã lưu. Đây là chức năng XEM LẠI/LUYỆN TẬP, không phải AI biết vị trí địch trong trận đang diễn ra.** Cầu nối AI cá nhân V2.2.6 và HUD chỉ số/cảnh báo máu hoạt động như trước; không có lời đọc vị trí đối phương tự động trên HUD.

**Thử chức năng:** cập nhật mã `git pull origin main`, chạy `.\\RUN_WINDOWS.cmd`. Trong Companion V1.4, bấm **Xem lại vị trí Rừng / Mid · nghe tiếng Việt**. Chọn **Mở ảnh trận đã lưu** (có thể chọn JPG được thu bởi công cụ V1.3); nhập thời điểm ảnh, bấm đúng biểu tượng tướng nhìn thấy trong ảnh để đặt dấu, chọn **Rừng địch** hoặc **Mid đối phương** cùng khu vực **Đường trên/giữa/dưới, sông, rừng**, sau đó bấm **Đọc vị trí đã đánh dấu trong ảnh**. Ứng dụng sẽ phát câu như “Trong ảnh đã lưu, bạn đánh dấu rừng địch ở đường giữa.” bằng giọng tiếng Việt miễn phí nếu tạo được âm thanh. Khi thử lần đầu, cần Internet để tạo câu qua dịch vụ miễn phí hoặc cài eSpeak NG để tạo giọng tiếng Việt ngoại tuyến; sau đó đọc từ tệp lưu trên máy. Nút **Dừng giọng xem lại** ngắt phát. Nếu vị trí chưa rõ, chọn **Không xác định**: ứng dụng sẽ không đưa ra câu đoán.

**Độ tin cậy:** vai trò, vị trí và thời điểm là **nhãn thủ công do người dùng chọn**, không phải kết quả AI nhận diện. Chọn sai vai trò/đường sẽ dẫn tới phát câu sai tương ứng; chức năng không tự xác minh ảnh. Bản này không chụp màn hình ngầm, không định vị tướng khuất tầm nhìn, không phát cảnh báo rừng đang gank, không tự đọc vị trí khi bạn đang chơi. Muốn xây dựng phiên bản tự nhận diện cần ảnh minimap thực tế đã gán nhãn chính xác và kiểm thử độ trễ, tỷ lệ nhận diện sai, đồng thời rà soát chính sách dành cho ứng dụng trong trận của Riot. Không nên bật tính năng thông báo vị trí đối thủ trong trận khi chưa chứng minh được cả độ chính xác và phạm vi sử dụng được phép.

---

## V1.3 — Thu thập ảnh minimap để luyện tập

**Đã thêm công cụ thu thập ảnh minimap để luyện tập AI; CHƯA có AI tự nhận diện rừng địch hoặc giao tranh.** Bản này tạo dữ liệu đầu vào thực tế phục vụ gán nhãn và kiểm thử một mô hình thị giác về sau, không giả định một thuật toán dò màu là trí tuệ nhân tạo hoặc một tướng vắng mặt là đang đi gank.

**Cách thử:** cập nhật nguồn trên máy Windows bằng `git pull origin main`, chạy `.\RUN_WINDOWS.cmd`. Trong cửa sổ Companion V1.3, chọn **Thu ảnh minimap để luyện tập AI (chỉ lưu trên máy)** → **Bắt đầu thu ảnh luyện tập** → xác nhận đồng ý → chuyển sang trận luyện tập hoặc video xem lại Liên Minh đang chạy ở chế độ Cửa sổ/Không viền. Chương trình chỉ lấy phần **góc dưới bên phải của vùng nội dung cửa sổ Liên Minh đang được chọn**; không quét hay ghi hình ứng dụng khác. Mỗi ba giây lưu một ảnh JPG tối đa 120 ảnh/phiên, tự dừng khi đủ; bạn có thể quay lại cửa sổ Recorder và nhấn **Dừng thu ảnh** bất cứ lúc nào. Nút **Mở thư mục ảnh đã lưu** mở thư mục phiên đó. Khi không có Liên Minh được chọn, chương trình chờ mà không thu ảnh.

**Lưu ở đâu:** `%LOCALAPPDATA%\XerathSupportAssistant\minimap-training\<thư mục phiên>`. Không tự gửi ảnh tới API AI Cá Nhân, Gemini, ChatGPT, GitHub hoặc dịch vụ trực tuyến; không chụp toàn màn hình và không can thiệp tiến trình, bộ nhớ hay thao tác game. Ảnh được lưu theo phiên vào ổ đĩa của bạn và **không tự xóa**: chỉ sử dụng ở buổi luyện tập hoặc xem lại khi bạn đồng ý lưu hình ảnh đó. Vùng cắt đang dùng tỷ lệ tương đối của cửa sổ, chưa được căn chỉnh theo mọi kiểu HUD, DPI, độ phân giải hay minimap ở góc khác. Trước khi chia sẻ ảnh cần xem lại nội dung và quyền riêng tư của người chơi khác.

**Còn thiếu để thành AI quan sát thực tế:** ảnh đã được gán nhãn vị trí/tên tướng thấy trên minimap và trạng thái tầm nhìn, bộ mô hình thị giác thực sự (ví dụ mô hình chạy trên máy dùng ONNX), đo độ chính xác trên ảnh thực tế và đo báo sai theo chuỗi ảnh. Khi mô hình chưa nhận ra icon rừng địch với bằng chứng rõ ràng, app sẽ **không nói rằng đã theo dõi rừng địch**, không suy ra vị trí trong sương mù chiến tranh và không phát cảnh báo tác chiến trong trận. Cầu nối hiện tại AI-Ca-Nhan chỉ tiếp nhận hai sự kiện đã xác nhận và không nhận ảnh. Việc chụp ảnh game không tự chứng minh tính phù hợp với mọi quy định ứng dụng của Riot; cần xem xét từng loại kết quả và hình thức sử dụng trước khi bật trong trận.

**Kiểm tra kỹ thuật:** Windows CI build WPF và chạy các kiểm thử core. Sau khi cập nhật mã, cần kiểm tra thủ công rằng ảnh JPG lưu đúng minimap trên độ phân giải và cách đặt minimap của máy bạn; nếu vùng ảnh trống/lệch hãy gửi một ảnh JPG mẫu đã kiểm tra không chứa nội dung riêng tư (không gửi thông tin tài khoản). Không nên coi việc build thành công là nhận diện hình ảnh thành công.

---

## V1.2 — HUD thông báo có giọng tiếng Việt

**Đã triển khai:** HUD vẫn cập nhật chỉ số cá nhân khoảng mỗi giây và nay theo dõi trạng thái chính Xerath từ HP: khi HP về 0, xóa cảnh báo máu dưới 30% cũ, ghim dòng **“Bạn đã bị hạ gục.”** cho đến lúc HP hồi phục. Khi hồi sinh, xóa dòng hạ gục và hiện **“Xerath đã hồi sinh.”** một lần. Không suy ra kẻ hạ gục, vị trí địch hoặc thời điểm hồi sinh tương lai.

**Giọng cảnh báo HUD:** âm thanh tiếng Việt *có thể bật/tắt độc lập* với bộ hẹn giờ. Có 7 câu cố định được tạo/lưu một lần trước khi chơi: hạ gục, hồi sinh, máu giảm nhanh, máu thấp, năng lượng thấp, mốc vàng và thông báo điểm hạ gục. Khi một cảnh báo tương ứng **thực sự được hiển thị trên HUD**, ứng dụng phát bản ghi đã lưu; không cần chuyển tab, không phải nhấn **Bắt đầu nhắc** và không gọi dịch vụ tạo giọng trong trận. Phần trăm máu, lượng vàng và mô tả trả về của AI cá nhân **vẫn được hiển thị đầy đủ bằng chữ**, nhưng câu đọc là **tóm tắt cố định theo loại sự kiện**, không đọc chính xác mọi con số động hoặc nguyên văn mọi câu trả về. Lời nhắc minimap theo giờ đang tắt mặc định trên HUD vẫn không được đọc bởi bộ đọc tình huống này.

**Cách chuẩn bị giọng (miễn phí, không có API key):** đóng phiên Companion cũ, cập nhật bản mới và mở Companion. Trong mục **2 · Giọng nói tiếng Việt**, nhấn **Tạo giọng tiếng Việt miễn phí** để lưu 7 câu HUD trước các lời nhắc theo giờ đã chọn, sau đó **Nghe thử**. Việc tạo một lần cần Internet cho bộ giọng trực tuyến miễn phí; nếu dịch vụ không trả âm thanh, có thể cài [eSpeak NG Windows x64](https://github.com/espeak-ng/espeak-ng/releases) và nhấn tạo lại: eSpeak NG có giọng tiếng Việt ngoại tuyến (hơi máy móc). Ở mục **Chỉ số trực tiếp & tổng hợp · tùy chỉnh HUD**, tích **Đọc tiếng Việt khi HUD có cảnh báo theo tình huống** và nhấn **Nghe thử cảnh báo HUD**. Chỉ khi tệp âm thanh tương ứng đã tạo xong mới có tiếng; nếu không thì cảnh báo chữ vẫn hiển thị và ứng dụng báo thiếu tệp. Không khẳng định tự tạo giọng thành công trên máy nếu nút nghe thử chưa phát tiếng.

**Không trùng bộ nhắc giờ:** nút **Bắt đầu nhắc** ở Companion chỉ dùng cho sáu loại lời nhắc *theo thời gian*, không cần bật để nghe các sự kiện của HUD. Nếu bạn không muốn hai luồng âm thanh phát đè lên nhau, đừng chạy bộ nhắc theo giờ khi đang dùng tiếng cảnh báo tình huống. Cảnh báo lời theo tình huống tôn trọng lựa chọn bật/tắt, tránh phát trùng trong khoảng thời gian ngắn; trạng thái hạ gục ưu tiên hơn thông báo cũ. Kết nối AI cá nhân là tùy chọn, chưa phải mô hình AI quan sát hình ảnh.

**Cập nhật Windows:** trong PowerShell, tại thư mục dự án Xerath chạy:

```powershell
git pull origin main
.\RUN_WINDOWS.cmd
```

Sau khi bạn nghe thử thành công, bật HUD và trở lại game ở chế độ Cửa sổ/Không viền. **Cần thử thực tế trên máy người dùng:** quy trình CI build WPF và chạy kiểm thử các thay đổi HP→0 và HP hồi phục, nhưng chưa kiểm chứng loa/phát giọng, tệp giọng trên máy và kết nối trong trận thực. Không bao gồm AI theo dõi rừng hoặc tự chỉ đường/ra lệnh dùng kỹ năng.

---

## V1.1 — Cầu nối AI cá nhân trên HUD

**Tích hợp bước đầu:** HUD hiện có thể **tùy chọn** gửi hai loại sự kiện đã xác nhận sang ứng dụng AI-Ca-Nhan đang chạy trên **cùng máy**: máu của chính bạn vừa giảm nhanh và điểm hạ gục đã được công bố. AI Cá Nhân nhận dữ liệu dạng JSON qua API cục bộ và trả lại lời nhắc tiếng Việt dạng mẫu cố định để hiển thị trong HUD. Nếu API không phản hồi trong 750 ms hoặc không chạy, Xerath vẫn hiện lời nhắc cục bộ như trước. Không truyền ảnh màn hình, video, tệp cá nhân, API key hoặc vị trí đối thủ; không cần AI trả phí để thử kết nối.

**Đây là kết nối API thử nghiệm, chưa phải trí tuệ nhân tạo quan sát trận đấu.** API của AI Cá Nhân hiện chỉ diễn đạt lại *hai tín hiệu đã xác nhận* theo mẫu an toàn, không gọi Gemini/OpenAI và không theo dõi rừng, đoán nơi giao tranh hoặc ra lệnh đi đâu/dùng Q/W/E/R. Khi bật chức năng kết nối, chỉ có hai sự kiện trên được gửi; thông tin vẫn phải lấy từ Riot Local API và bộ phát hiện cục bộ trước khi gửi.

**Cách bật kết nối hai dự án:**

1. Trên máy Windows, cập nhật dự án **AI-Ca-Nhan** tại thư mục mã nguồn của chính nó bằng `git pull origin main`, rồi chạy `dotnet run --project src/PersonalAI.Web --launch-profile PersonalAI.Web`. Giữ cửa sổ PowerShell đó mở. Địa chỉ HTTP cục bộ mặc định là `http://127.0.0.1:5188`. Nếu bạn chạy cấu hình hoặc cổng khác, cầu nối chưa hỗ trợ thay đổi cổng.
2. Đóng Xerath Support Assistant đang chạy; trong thư mục dự án Xerath chạy `git pull origin main`, rồi `.\RUN_WINDOWS.cmd`.
3. Nhấn **Chỉ số trực tiếp & tổng hợp · tùy chỉnh HUD** → **Kiểm tra kết nối AI cá nhân**. Khi dòng trạng thái báo đã kết nối, tích **Thử cầu nối AI cá nhân trên máy**. Sau đó nhấn **Bật HUD trên game**, chuyển lại Liên Minh ở chế độ **Cửa sổ / Không viền**.
4. Khi bạn gặp tình huống khiến HP của chính bạn giảm nhanh hoặc có điểm hạ gục *đã xảy ra*, thông báo sẽ qua cầu nối khi đang bật; nếu máy chủ AI không chạy vẫn có thông báo nội bộ. Chỉ bấm **Bắt đầu nhắc** trong Companion khi cần các lời nhắc *bằng giọng nói theo giờ*; tính năng này chưa tự đọc các câu trả về từ AI Cá Nhân.

**An toàn và giới hạn:** chỉ kết nối đến `http://127.0.0.1:5188`, chỉ khi người dùng chủ động bật tùy chọn. API nhận tín hiệu trên máy chưa có xác thực giữa các tiến trình; **không mở port ra mạng ngoài hoặc Internet**. Hãy thử trong buổi luyện tập và kiểm tra xem HUD cập nhật đúng, không che game hoặc lặp thông báo. Nếu AI cá nhân chạy bằng HTTPS ở cổng 7188 mà không mở HTTP 5188, nút kiểm tra sẽ báo chưa kết nối. Chưa kiểm tra tích hợp thực tế trên máy bạn.

---

## V1.0 — HUD ưu tiên tình huống thực tế

**V1.0 — phần đã triển khai:** đọc chỉ số *chính bạn* từ API trận cục bộ khoảng 1 giây/lần và tự nhận ra khi máu vừa giảm ít nhất 22% tối đa trong 2,5 giây (ít nhất 100 HP). Khi có thay đổi được xác nhận, HUD thông báo ngắn bằng tiếng Việt ngay trên màn hình game, có chống lặp khoảng 8 giây. Cảnh báo máu xuống dưới 30%, năng lượng dưới 25%, vàng đạt 2.500 vẫn hoạt động như trước. Số liệu API có thể trả về trễ hoặc gián đoạn; đây là quan sát hai mẫu gần nhau, **không phải thuật toán phát hiện tướng địch đang ở đâu, cũng không phải nhận diện giao tranh**.

**Lời nhắc đúng tình huống trước:** lời nhắc *theo giờ* trên HUD mặc định **tắt**, chỉ hiện các thông báo theo chỉ số hoặc sự kiện trận thực. Trong cửa sổ **Chỉ số trực tiếp & tổng hợp**, bạn có thể tích **Hiện thêm lời nhắc theo thời gian trên HUD** nếu vẫn muốn nhắc nhìn minimap/mắt theo chu kỳ. Lời nhắc bằng giọng nói trong mục 3 của Companion vẫn là bộ hẹn giờ riêng; nếu nhấn **Bắt đầu nhắc**, tiếng sẽ phát theo lịch như cũ, không tự biến thành giọng đọc cảnh báo theo tình huống. Có thể tắt lựa chọn **Cảnh báo khi máu của bạn giảm nhanh** nếu không muốn nhận thông báo này.

**Sự kiện hạ gục:** HUD vẫn có thể hiển thị điểm hạ gục *vừa xảy ra* từ `/liveclientdata/eventdata` (nếu bật), không suy ra Mid/Top/Bot đang giao tranh chỉ từ kill feed. Đừng hiểu thông báo nhanh về mất máu là bằng chứng rừng địch đã xuất hiện.

**Vẫn chưa có:** hệ thống AI quan sát từng khung hình và nhận diện **vị trí rừng địch**, xác định **vị trí giao tranh từ minimap**, hoặc tự chọn **đường di chuyển và kỹ năng Q/W/E/R**. Muốn hiện những thông tin đó một cách đáng tin cậy cần (1) nguồn ảnh game phù hợp, (2) tập dữ liệu gán nhãn champion/minimap theo độ phân giải và chế độ giao diện, (3) mô hình nhận diện được kiểm chứng cả tỉ lệ báo sai và độ trễ, (4) rà soát quy định ứng dụng trong trận của Riot. Hiện tại không có model CV đã huấn luyện trong repo, nên **không thể nói chức năng AI theo dõi rừng/giao tranh đã hoàn thành**. Đây là phiên bản cải thiện khả năng phát hiện **tình huống thực từ dữ liệu cá nhân** trên HUD; không quảng cáo suy đoán thành quan sát.

**Cập nhật trên Windows:** đóng ứng dụng đang chạy, mở PowerShell trong thư mục dự án rồi chạy:

```powershell
git pull origin main
.\RUN_WINDOWS.cmd
```

Chờ tiêu đề **Companion V1.0**, nhấn **Bật HUD theo tình huống trong game** rồi quay lại game ở chế độ **Cửa sổ** hoặc **Không viền**. HUD có thể hiện cảnh báo sát thương theo sự kiện mà không yêu cầu API key FPT.AI và không cần cài giọng nói. Để nghe nhắc theo giờ, thực hiện riêng phần **2. Giọng nói tiếng Việt** và **3. Bắt đầu nhắc**.

**Kiểm thử tự động:** workflow `.github/workflows/windows-build.yml` build WPF trên Windows và chạy các phép thử core. Bộ thử được bổ sung cho mất máu đột ngột, tránh báo liên tục, kết thúc trận và mở trận mới. Chưa thử điều kiện trận thật và chưa kiểm chứng nhận diện hình ảnh. Để xem quá trình build, mở trang Actions trong repo; nếu chạy `git pull` báo không thấy thay đổi, kiểm tra xem cửa sổ đang chạy đã đóng trước khi mở lại.

---

## V0.9.1 — Sửa lỗi giọng nói tiếng Việt miễn phí

**Vấn đề đã sửa:** V0.9 dựa vào dịch vụ giọng Hoài My qua Edge TTS, nhưng trên máy người dùng dịch vụ không trả về âm thanh. V0.9.1 **không còn dùng thư viện Edge TTS đó**. Khi nhấn **Tạo giọng tiếng Việt miễn phí**, ứng dụng thử tạo MP3 tiếng Việt từ dịch vụ Google Translate TTS không cần API key; nếu không truy cập được, tự động chuyển sang **eSpeak NG đã cài trên máy** để tạo WAV tiếng Việt hoàn toàn offline. Giọng eSpeak NG nghe máy móc hơn giọng trực tuyến nhưng không phụ thuộc dịch vụ chuyển văn bản thành giọng nói khi đã cài đặt. Mỗi câu được lưu vào `%LOCALAPPDATA%\XerathSupportAssistant\voice\free-vietnamese-v2` để lần chơi sau không cần tạo lại.

**Cách dùng trên Windows:** Đóng phần mềm cũ rồi chạy `git pull origin main` và `.\RUN_WINDOWS.cmd` trong thư mục dự án. Đợi cửa sổ hiển thị **V0.9.1**. Giữ tích **Giọng đọc tiếng Việt miễn phí**, nhấn **Tạo giọng tiếng Việt miễn phí**, đợi hiện thông báo đủ 6/6 câu nhắc, nhấn **Nghe thử** rồi **Bắt đầu nhắc**. HUD và bộ nhắc giọng hoạt động độc lập; muốn nghe tiếng bạn cần nhấn **Bắt đầu nhắc** ở mục 3. Các lời nhắc HUD vẫn hiện chữ kể cả chưa tạo được âm thanh.

**Nếu giọng trực tuyến vẫn báo lỗi:** tải và cài **eSpeak NG x64** (bản phát hành Windows miễn phí tại https://github.com/espeak-ng/espeak-ng/releases), mở lại Companion và nhấn **Tạo giọng tiếng Việt miễn phí**. Ứng dụng sẽ tìm `espeak-ng.exe` trong `C:\Program Files\eSpeak NG` hoặc `C:\Program Files (x86)\eSpeak NG`, dùng ngôn ngữ **vi** và lưu WAV cho 6 câu nhắc. Có thể đọc offline sau khi cài và không cần tài khoản/API key. Nếu cài vào đường dẫn khác, phần mềm hiện chưa tự phát hiện được. **Không cài nếu bạn không muốn dùng phần mềm bên ngoài**; khi đó bạn vẫn có thể dùng HUD chữ hoặc thử lại giọng trực tuyến.

**Lưu ý về độ tin cậy:** Google Translate TTS dành cho người tiêu dùng không phải API được cam kết duy trì cho ứng dụng của bên thứ ba, vì vậy có thể chặn yêu cầu hoặc ngừng hoạt động. eSpeak NG là bộ tổng hợp giọng nói nguồn mở hỗ trợ tiếng Việt; chất giọng có tính tổng hợp và phát âm có thể chưa tự nhiên. Không hứa bản mới đã phát tiếng thành công trên máy bạn: cần **Nghe thử** xác nhận. Không sử dụng tiếng Anh thay thế khi tiếng Việt chưa sẵn sàng. Câu nhắc gửi lên dịch vụ trực tuyến là các câu cố định trong ứng dụng, không phải mật khẩu hay nội dung cá nhân.

**Kiểm thử:** chạy `dotnet build src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj` trên Windows. Chưa có kết quả thử phát giọng thành công trên máy người dùng. Nếu cả hai lựa chọn gặp lỗi, gửi ảnh **dòng thông báo lỗi** và ảnh thư mục cài eSpeak NG (nếu đã cài); không cần bất kỳ API key nào.

---

## V0.9 — Giọng nói tiếng Việt miễn phí (đã được V0.9.1 thay thế)

**Thay đổi ở V0.9:** hệ thống nhắc bằng giọng nói không còn yêu cầu API key FPT.AI. Trong Companion tích chọn **Giọng nữ tiếng Việt miễn phí (Hoài My, âm lượng 70%)**, bấm **Tạo giọng tiếng Việt miễn phí**, chờ hoàn tất, sau đó bấm **Nghe thử** và **Bắt đầu nhắc**. Việc chuẩn bị âm thanh cần Internet ở lần đầu; các câu MP3 đã lưu được phát offline khi chơi, không tốn phí dịch vụ trên mỗi lần phát. Giọng Hoài My được liệt kê là giọng nữ tiếng Việt; **chưa xác nhận giọng vùng miền Bắc** trên máy người dùng.

**Nguồn tạo giọng:** thư viện cộng đồng `Edge_tts_sharp` 1.1.7 dùng dịch vụ đọc văn bản trực tuyến của Microsoft Edge, không yêu cầu API key hay tài khoản trả phí. Đây **không phải API công khai có cam kết duy trì hoặc được Microsoft bảo đảm miễn phí lâu dài**; có thể không hoạt động vì thay đổi phía dịch vụ, mạng hoặc giới hạn sử dụng. Khi đó phần mềm hiện thông báo lỗi, không tự dùng giọng tiếng Anh và **HUD/nhắc bằng chữ vẫn chạy bình thường**. Đừng nhập bí mật trong câu nhắc: câu chữ sẽ gửi tới dịch vụ trực tuyến để tạo âm thanh, sau đó chỉ lưu MP3 trên máy bạn.

**Cập nhật trên Windows:** đóng ứng dụng cũ; tại thư mục repository chạy:

```powershell
git pull origin main
.\RUN_WINDOWS.cmd
```

Lần đầu biên dịch cần mạng để tải gói NuGet miễn phí. Tại phần **2 · Giọng nói tiếng Việt**, giữ tích giọng nói và chọn **Tạo giọng tiếng Việt miễn phí**. Chờ dòng trạng thái báo đã tạo đủ câu nhắc, nhấn **Nghe thử**, nhấn **Bắt đầu nhắc**, rồi quay lại game. Để chỉ dùng HUD thông báo bằng chữ, bỏ tích giọng nói; HUD không phụ thuộc vào mạng hoặc dịch vụ tạo âm thanh sau khi đã lấy được dữ liệu trận từ API cục bộ.

**Vị trí tệp:** `%LOCALAPPDATA%\XerathSupportAssistant\voice\edge-hoaimy-v1`. Tệp MP3 đã tạo được tái sử dụng trong các lần sau; câu nhắc chưa tạo sẽ cần Internet để tạo thêm. Tệp FPT.AI từ V0.3, nếu có, không bị xóa nhưng V0.9 không tự sử dụng. Giọng nói hiện áp dụng cho **bộ nhắc theo lịch sau khi nhấn Bắt đầu nhắc**; các thông báo chỉ số và hạ gục mới trên HUD vẫn **chỉ hiển thị bằng chữ**. Nếu dùng HUD nhắc giờ cùng bộ nhắc giọng, có thể gặp thông báo chữ và tiếng cùng lúc.

**Kiểm tra:** `dotnet build src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj` và `dotnet run --project tests/XerathAssistant.CoreTests/XerathAssistant.CoreTests.csproj`. Mã nguồn đã cập nhật nhưng **chưa xác nhận đã biên dịch hoặc phát giọng thành công trên máy Windows của bạn**. Nếu lỗi biên dịch hoặc dịch vụ tạo giọng không trả lời, chụp dòng báo lỗi (không cần cung cấp bất kỳ API key nào).

---

## V0.8 — HUD thông báo và cập nhật khoảng 1 giây

**Mới trên màn hình game:** HUD không chỉ hiện các chỉ số cá nhân mà còn tự hiện lời nhắc đã chọn và cảnh báo khi máu/năng lượng của **chính bạn** vừa xuống dưới ngưỡng, hoặc vàng hiện có vừa đạt ngưỡng tham khảo. HUD tự ẩn thông báo sau 5–7 giây, chống lặp khi chỉ số vẫn thấp. Không cần chuyển tab để xem các thông báo. Vẫn có thể dùng HUD chữ mà **không có FPT.AI API key**.

**Tần suất:** tự đọc chỉ số cá nhân theo chu kỳ khoảng **1 giây/lần** thay cho 5 giây; lời nhắc trên HUD được kiểm tra cùng chu kỳ. API qua HTTPS loopback có thể trả lời chậm hơn 1 giây; cập nhật không được bảo đảm liên tục từng khung hình. Sự kiện hạ gục đã được Riot công bố được đọc riêng khoảng 2 giây/lần (nếu đã bật); không làm chậm quá trình lấy chỉ số cá nhân.

**Thông tin trận:** Có thể bật thông báo về **điểm hạ gục vừa xảy ra** từ endpoint công khai `/liveclientdata/eventdata`. Đây là sự kiện đã hoàn tất, **KHÔNG** có bằng chứng giao tranh đang diễn ra, không cung cấp vị trí Top/Mid/Bot và **KHÔNG** theo dõi vị trí hoặc đường di chuyển rừng địch. Trong giao diện Chỉ số trực tiếp, bỏ chọn tùy chọn thông báo hạ gục nếu không cần. Không tự đánh dấu vị trí rừng ngoài tầm nhìn, không tự suy luận vị trí theo thời gian thực, không can thiệp Vanguard.

**Cách cập nhật trên Windows:** Đóng trợ lý cũ; tại thư mục repo, chạy:

```powershell
git pull origin main
.\RUN_WINDOWS.cmd
```

Trong game dùng **Cửa sổ (Windowed)** hoặc **Không viền (Borderless)**. Từ Companion nhấn **Bật HUD và thông báo trên game**; các mục nhắc tích chọn ở Companion được hiển thị bằng chữ trên HUD theo lịch khi HUD đang bật, không cần nhấn riêng nút Bắt đầu nhắc. Chỉ số cá nhân và thông báo hạ gục đã hoàn tất đến từ API cục bộ, không phải nhận diện hình ảnh. Muốn thay góc/tắt HUD hoặc tắt thông báo hạ gục, vào **Chỉ số trực tiếp & tổng hợp · tùy chỉnh HUD**. Bật bộ hẹn giờ ở Companion riêng chỉ khi muốn sử dụng chức năng nhắc ngoài HUD/giọng FPT.AI; không cần API key FPT.AI để dùng HUD. Để tránh lời nhắc trùng, có thể không chạy cả hai bộ hẹn giờ cùng lúc.

**Hạn chế của phiên bản:** chưa có tự nhận diện giao tranh Top/Mid, hay theo dõi rừng tự động. Live Client Data API không cung cấp vị trí rừng hoặc tín hiệu vị trí giao tranh trong dữ liệu sự kiện đã công bố; không tự đặt nhãn “có giao tranh” khi chỉ có thông báo hạ gục. Những chức năng này cần nguồn quan sát hợp lệ, kiểm thử độ chính xác và xác nhận phạm vi cho phép trước khi sử dụng trong trận. Công cụ không tự ra quyết định đẩy lính, đi gank hay ngắm Q/W/E/R.

Mã nguồn mới: `src/XerathAssistant.Core/PersonalStatAlerts.cs`, `src/XerathAssistant.Core/PublicKillEventTracker.cs`, `src/XerathAssistant.Desktop/SelfStatsWindow.xaml.cs`, `src/XerathAssistant.Desktop/SelfStatsHudWindow.xaml.cs` và `.xaml`. Bộ kiểm thử core đã được bổ sung; **chưa xác nhận bản mới biên dịch/chạy thành công trên máy Windows của bạn**.

---

## V0.7 — HUD chỉ số trên game

**Không cần Alt + Tab để xem chỉ số:** Phiên bản này bổ sung nút **Bật HUD chỉ số trên game** trong giao diện Companion. Sau khi vào trận, bật nút một lần rồi quay lại Liên Minh ở chế độ **Không viền (Borderless)**. HUD nhỏ sẽ hiển thị ở góc trên phải: máu, năng lượng, cấp độ, sức mạnh phép thuật, vàng hiện có và đồng hồ trận. Các chỉ số được cập nhật 5 giây/lần qua Riot Live Client Data API cục bộ của **chính người chơi**; không đọc thế lính, vị trí đối thủ hoặc thông tin ẩn. Không cần API key FPT.AI để sử dụng HUD.

**Cách dùng:** Đóng ứng dụng cũ, mở PowerShell trong thư mục repository, chạy `git pull origin main` và `.\RUN_WINDOWS.cmd`. Trong Liên Minh, nhấn Esc → Video → chuyển chế độ cửa sổ sang **Không viền (Borderless)**. Trong Companion nhấn **Bật HUD chỉ số trên game**, quay lại trận. Khi game là cửa sổ được chọn, HUD sẽ xuất hiện; khi Alt + Tab ra ngoài, HUD tự ẩn để không che các ứng dụng khác. HUD không nhận click chuột nhằm tránh cản trở điều khiển game.

**Tùy chỉnh/tắt HUD:** Trong Companion chọn **Chỉ số trực tiếp & tổng hợp · tùy chỉnh HUD**, thay đổi góc HUD (trên phải, trên trái, dưới trái, dưới phải) hoặc nhấn **Tắt HUD**. Bảng thống kê đầy đủ vẫn có sẵn khi cần xem lại, nhưng không cần mở bảng đó để xem HUD khi chơi. HUD dùng các số liệu lấy trong quá trình trận đang chạy; nếu mất kết nối API, hiện cảnh báo giá trị có thể đã cũ.

**Giới hạn:** WPF topmost không bảo đảm hiển thị trên **Fullscreen độc quyền**; hãy thử chế độ Không viền trước. HUD có thể cần điều chỉnh vị trí nếu che thành phần giao diện của Liên Minh; hiệu quả hiển thị phụ thuộc cấu hình Windows và game. Chức năng này chưa được xác nhận biên dịch và chạy trên máy Windows của bạn. Không sử dụng phần mềm để can thiệp Vanguard, game memory hay điều khiển tướng. Việc một HUD chỉ hiển thị thông tin cá nhân không đồng nghĩa sản phẩm đã được Riot chính thức phê duyệt.

---

## V0.6 — Live Self Stats

**Mới:** nút **Chỉ số trực tiếp & tổng hợp** mở cửa sổ đọc tự động chỉ số của **chính bạn** đang chơi, mỗi 5 giây, bằng Riot Live Client Data API trên máy (`https://127.0.0.1:2999`). Không cần tự nhập hoặc xem lại video. Cửa sổ hiển thị đồng hồ trận, cấp độ, máu, năng lượng, vàng hiện có, sức mạnh phép thuật, và tổng hợp từ các lần đọc (máu/năng lượng thấp nhất đã thấy; ước lượng khoảng thời gian quan sát khi mana dưới 25%; lượng vàng hiện có cao nhất đã thấy). Không cần API key; không lưu số liệu lên máy chủ; dữ liệu lịch sử chỉ nằm trong bộ nhớ cho tới khi đóng cửa sổ.

**Cài đặt/cập nhật Windows:** Đóng chương trình cũ, tại thư mục dự án chạy:

```powershell
git pull origin main
.\RUN_WINDOWS.cmd
```

Vào **trận thực tế** với Xerath, mở Companion rồi nhấn **Chỉ số trực tiếp & tổng hợp**. Nếu trò chơi chưa khởi chạy trận, cửa sổ sẽ báo chưa đọc được. Nhấn **Cập nhật ngay** hoặc bật **Cập nhật chỉ số mỗi 5 giây**; khi kết thúc trận hãy xem phần **Tổng hợp tự động** trước khi đóng cửa sổ. Có thể nhấn **Xóa thống kê phiên** để bắt đầu thống kê mới. Khi mở trận mới, lịch sử trước đó tự reset nếu đồng hồ trận quay về 0; ứng dụng không ghi file thống kê.

**Dữ liệu và giới hạn:** Chỉ gọi hai endpoint tài liệu hóa `/liveclientdata/activeplayer` và `/liveclientdata/gamestats` qua HTTPS loopback. Client Liên Minh sử dụng chứng chỉ tự ký nên ứng dụng chỉ chấp nhận ngoại lệ chứng chỉ cho địa chỉ `127.0.0.1:2999`; không tắt kiểm tra TLS cho các dịch vụ khác, không truy cập bộ nhớ game hay Vanguard. Nội dung chỉ hiển thị **thông tin cá nhân vốn đã nhìn thấy trong game**, không thu thập vị trí hay hồi chiêu đối phương, không thông báo tự động giao tranh Top/Mid, không phân tích thế lính và không hướng dẫn giao tranh theo thời gian thực. Đây **không phải** công cụ tự động cho lời khuyên chiến thuật. Việc sử dụng dịch vụ/trình bày thông tin cần tuân thủ chính sách hiện hành của Riot; hãy đăng ký sản phẩm trên Riot Developer Portal trước khi phát hành cho người chơi khác.

Tài liệu endpoint chính thức: https://developer.riotgames.com/docs/lol#game-client-api và https://developer.riotgames.com/docs/lol#live-client-data-api

**Kiểm tra:** `dotnet build src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj` và `dotnet run --project tests/XerathAssistant.CoreTests/XerathAssistant.CoreTests.csproj`. Các bài kiểm tra mới xác nhận đọc JSON có cấu trúc API, số phần trăm HP/mana và thống kê phiên. Chưa xác nhận build/chạy thành công trên máy Windows của bạn.

---

## V0.5 — Wave & Fight Advisor

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
