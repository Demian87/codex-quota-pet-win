using System.Globalization;

namespace QuotaWisp;

public static class L
{
    private static UiLanguage _language = UiLanguage.Auto;
    public static void SetLanguage(UiLanguage language) => _language = language;
    public static bool IsRussian => _language == UiLanguage.Russian ||
        (_language == UiLanguage.Auto && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru");

    private static readonly Dictionary<string, (string En, string Ru)> Values = new()
    {
        ["app"] = ("Quota Wisp", "Квота-Огонёк"),
        ["available"] = ("AVAILABLE", "ДОСТУПНО"),
        ["weekly"] = ("Weekly", "Недельный"),
        ["resetcredits"] = ("MANUAL RESETS", "РУЧНЫЕ СБРОСЫ"),
        ["reset"] = ("Reset", "Сброс"),
        ["standard"] = ("STANDARD", "ОБЫЧНЫЙ"), ["turbo"] = ("TURBO", "ТУРБО"),
        ["connecting"] = ("Connecting to Codex…", "Подключение к Codex…"),
        ["reconnecting"] = ("Reconnecting…", "Переподключение…"),
        ["disconnected"] = ("Codex is unavailable", "Codex недоступен"),
        ["refresh"] = ("Refresh now", "Обновить сейчас"),
        ["show"] = ("Show pet", "Показать питомца"), ["hide"] = ("Hide pet", "Скрыть питомца"),
        ["size"] = ("Size", "Размер"), ["small"] = ("Small", "Маленький"),
        ["medium"] = ("Medium", "Средний"), ["large"] = ("Large", "Большой"),
        ["lock"] = ("Lock position", "Закрепить позицию"),
        ["clickthrough"] = ("Click-through", "Пропускать клики"),
        ["codexactive"] = ("Show only while Codex is active", "Показывать только при активном Codex"),
        ["fullscreen"] = ("Hide in fullscreen apps", "Скрывать в полноэкранных приложениях"),
        ["autostart"] = ("Launch with Windows", "Запускать с Windows"),
        ["history"] = ("Show quota history", "Показывать историю квоты"),
        ["clearhistory"] = ("Clear quota history", "Очистить историю квоты"),
        ["tooltip"] = ("Tooltip style", "Стиль подсказки"),
        ["smooth"] = ("Smooth", "Обычный"), ["pixel"] = ("Pixel", "Пиксельный"),
        ["language"] = ("Language", "Язык"), ["auto"] = ("System", "Системный"),
        ["english"] = ("English", "Английский"), ["russian"] = ("Russian", "Русский"),
        ["codexpath"] = ("Select Codex executable…", "Выбрать исполняемый файл Codex…"),
        ["appearance"] = ("Appearance", "Внешний вид"),
        ["objects"] = ("Object mix", "Набор объектов"),
        ["behavior"] = ("Behavior", "Поведение"),
        ["quit"] = ("Quit", "Выход"),
        ["checkupdates"] = ("Check for updates…", "Проверить обновления…"),
        ["updatechecking"] = ("Checking for updates…", "Проверяем обновления…"),
        ["updatetitle"] = ("Quota Wisp update", "Обновление Квота-Огонька"),
        ["updatecurrent"] = ("You already have the latest version ({0}).", "У вас уже установлена последняя версия ({0})."),
        ["updateavailable"] = ("Version {0} is available (installed: {1}). Download, verify and install it now?", "Доступна версия {0} (установлена: {1}). Скачать, проверить и установить её сейчас?"),
        ["updatedownloading"] = ("Downloading and verifying the update…", "Скачиваем и проверяем обновление…"),
        ["updateready"] = ("The verified update is ready. Quota Wisp will save its history, close, install the update and restart. Continue?", "Проверенное обновление готово. Квота-Огонёк сохранит историю, закроется, установит обновление и перезапустится. Продолжить?"),
        ["updateerror"] = ("The update was not installed. Quota Wisp remains open.\n\n{0}", "Обновление не установлено. Квота-Огонёк продолжает работу.\n\n{0}"),
        ["updateinstallerror"] = ("The update could not be installed. The previous version was restored. Details were written to the local update log.", "Не удалось установить обновление. Предыдущая версия восстановлена. Подробности записаны в локальный журнал обновления."),
        ["unknown"] = ("unknown", "неизвестно"),
        ["historyempty"] = ("History is being collected", "История накапливается"),
        ["historytitle"] = ("24H BURN", "РАСХОД · 24Ч"),
        ["historynow"] = ("NOW", "СЕЙЧАС"),
        ["history24h"] = ("−24H", "−24Ч"),
        ["lowquota"] = ("Codex quota is running low: {0}% remaining.", "Квота Codex заканчивается: осталось {0}%.")
    };

    public static string T(string key) => Values.TryGetValue(key, out var value)
        ? (IsRussian ? value.Ru : value.En) : key;
}
