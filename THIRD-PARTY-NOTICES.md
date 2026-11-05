# Сторонние компоненты

Проект Tempo Karaoke использует следующие сторонние библиотеки. При распространении
приложения приложите этот файл и тексты соответствующих лицензий.

| Компонент | Лицензия | Назначение |
|---|---|---|
| LibVLCSharp / LibVLCSharp.WinUI | LGPL-2.1-or-later | управление libvlc из .NET |
| VideoLAN.LibVLC.Windows (libvlc 3.0.24) | LGPL-2.1-or-later | декодирование видео и звука |
| QRCoder | MIT | QR-коды пульта и Wi-Fi |
| CommunityToolkit.WinUI.Controls.SettingsControls | MIT | карточки параметров |
| Microsoft.WindowsAppSDK | MIT | WinUI 3, окна, dispatcher |
| Microsoft.Data.Sqlite / SQLitePCLRaw | MIT / SQLite public domain | кэш медиатеки |
| xUnit | Apache-2.0 | тесты |

libvlc распространяется по LGPL: при поставке приложения должна оставаться
возможность заменить библиотеки libvlc/LibVLCSharp, а уведомления об авторских
правах и лицензии должны быть включены в поставку. Не используйте бренд VLC как
название или логотип приложения.
