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
        ["fullscreen"] = ("Hide in fullscreen apps", "Скрывать в полноэкранных приложениях"),
        ["autostart"] = ("Launch with Windows", "Запускать с Windows"),
        ["history"] = ("Show quota history", "Показывать историю квоты"),
        ["clearhistory"] = ("Clear quota history", "Очистить историю квоты"),
        ["tooltip"] = ("Tooltip style", "Стиль подсказки"),
        ["smooth"] = ("Smooth", "Обычный"), ["pixel"] = ("Pixel", "Пиксельный"),
        ["language"] = ("Language", "Язык"), ["auto"] = ("System", "Системный"),
        ["english"] = ("English", "Английский"), ["russian"] = ("Russian", "Русский"),
        ["codexpath"] = ("Select Codex executable…", "Выбрать исполняемый файл Codex…"),
        ["objects"] = ("Object mix", "Набор объектов"), ["quit"] = ("Quit", "Выход"),
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
