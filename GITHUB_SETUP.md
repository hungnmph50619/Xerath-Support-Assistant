# Đưa Xerath Support Assistant lên GitHub

## Tạo repository (chỉ lần đầu)

1. Mở https://github.com/new bằng tài khoản GitHub của bạn.
2. Repository name: `Xerath-Support-Assistant`. Chọn **Private** nếu chưa muốn công khai mã nguồn.
3. Không chọn "Add a README file", ".gitignore" hoặc license vì thư mục dự án đã có README và .gitignore.
4. Nhấn **Create repository**. Sau đó mở PowerShell trong thư mục vừa giải nén (nơi có README.md và `src`).

## Đẩy mã nguồn lên GitHub

Cài Git for Windows nếu chưa có: https://git-scm.com/downloads/win .

Chạy lần lượt:

```powershell
git init -b main
git add .
git commit -m "Initial Xerath Support Assistant V0.1"
git remote add origin https://github.com/hungnmph50619/Xerath-Support-Assistant.git
git push -u origin main
```

Nếu Git thông báo thiếu tên hoặc email khi commit, chạy `git config --global user.name "Ten cua ban"` và `git config --global user.email "email-github-cua-ban"`, rồi chạy lại lệnh commit và push. GitHub có thể mở cửa sổ đăng nhập; không chia sẻ mật khẩu hoặc token với người khác.

## Tạo nhánh phát triển (sau khi đã push main)

```powershell
git switch -c develop
git push -u origin develop
```

## Những lần chỉnh sửa sau

```powershell
git switch develop
git pull
git add .
git commit -m "Describe your change"
git push
```

Khi cần đưa thay đổi ổn định vào main, tạo Pull Request từ `develop` sang `main` trên GitHub.

## Chạy ứng dụng trên Windows

```powershell
dotnet run --project src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj
```

## Ghi chú

- Đây là mã nguồn ứng dụng luyện ngắm độc lập V0.1, chưa nhận diện và hỗ trợ ngắm trực tiếp trong LoL.
- Workflow `.github/workflows/windows-build.yml` chạy kiểm tra build trên Windows và chương trình kiểm thử core sau khi push; chưa có kết quả CI trước lần chạy đầu tiên.
- Không đẩy lên GitHub mật khẩu, token, tệp `.env` hay thư mục chứa dữ liệu cá nhân.
