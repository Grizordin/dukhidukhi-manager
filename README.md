# Духи Духи менеджер

Менеджер расширений для локальной копии AnimeSSS на Windows x64.

## Обновления

GitHub Releases содержит пакеты для обновления уже установленного менеджера. Скачайте `DukhiDukhi-Manager-Setup.exe` из [релиза](https://github.com/Grizordin/dukhidukhi-manager/releases/latest) и выберите папку имеющейся установки. Менеджер 0.7.31 и новее проверяет релизы при запуске и устанавливает более новую версию автоматически. Проверка происходит в окне менеджера, когда клиент AnimeSSS завершён. Пакет заменяет программу менеджера, сохраняя уже подключённые расширения и профиль.

## Сборка пакета для GitHub

В `dist` нужны `Microsoft.Web.WebView2.Core.dll` и `Microsoft.Web.WebView2.WinForms.dll` из SDK Microsoft WebView2. Пакет использует .NET Framework 4.x и Inno Setup 6.

```powershell
.\build.ps1 -ManagerOnly
ISCC.exe /DManagerOnly DukhiDukhi.iss
```

Для выпуска увеличьте `VersionText` в `ManagerUpdate.cs`, пересоберите пакет и прикрепите `DukhiDukhi-Manager-Setup.exe` к стабильному релизу с соответствующим тегом `vX.Y.Z`. Менеджер проверяет размер и SHA-256, которые GitHub публикует в метаданных файла.
