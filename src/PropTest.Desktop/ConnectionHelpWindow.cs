using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class ConnectionHelpWindow : Window
{
    public ConnectionHelpWindow()
    {
        Title = "Як підключити плату"; Width = 620; Height = 590;
        MinWidth = 420; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var close = new Button { Content = "Зрозуміло", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Перше підключення", FontSize = 24, FontWeight = FontWeight.SemiBold });
        foreach (var text in new[] {
            "1. Підключіть ESP32 до ноутбука USB-кабелем із передаванням даних. У програмі виберіть «USB · кабель» та дочекайтеся підключення.",
            "2. Відкрийте «Wi-Fi через роутер». Введіть назву та пароль мережі 2,4 ГГц і натисніть «Зберегти мережу на платі». Дочекайтеся повідомлення про підключення до роутера.",
            "3. Натисніть «Відключити» або від’єднайте USB-кабель і підключіть плату до павербанка. Дочекайтеся від’єднання в програмі. Виберіть «Wi-Fi · мережа», відкрийте плашку зі стрілкою, виберіть свою плату та натисніть «Підключити». Ноутбук має бути в тій самій локальній мережі; він може використовувати 5 ГГц цього роутера.",
            "Галочка «Підключатися автоматично» знаходиться всередині вибраної плати. Без галочки натискайте «Підключити» вручну. Наступні рази: увімкніть живлення плати. З галочкою «Підключатися автоматично» програма знайде останню вибрану плату за збереженим ім’ям. Повторно вводити IP чи пароль не потрібно.",
            "Інша плата: виберіть її у списку й натисніть «Підключити». Попереднє з’єднання закриється. Галочка зберігається окремо для кожної плати; автоматично підключається лише остання вибрана.",
            "«Відключити» залишає плату в списку, але призупиняє автопідключення на цей запуск програми. Щоб відновити його, натисніть «Підключити» або вимкніть і знову увімкніть галочку.",
            "Якщо зв’язку немає: перевірте живлення плати та спільну мережу. Гостьова мережа з ізоляцією пристроїв може блокувати зв’язок. Нова плата додається через USB; список містить збережені плати, а не всі пристрої поблизу. Підключення не запускає мотор."
        }) panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(close); Content = new ScrollViewer { Content = panel };
    }
}

public sealed class BoardNameWindow : Window
{
    public BoardNameWindow(MainViewModel vm)
    {
        Title = "Змінити назву плати"; Width = 460; Height = 220; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var input = new TextBox { Text = vm.SelectedBoard?.Name, MaxLength = 60, Watermark = "Наприклад: Мій стенд" };
        var save = new Button { Content = "Зберегти" };
        var cancel = new Button { Content = "Скасувати" };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) { input.Focus(); return; } vm.BoardName = input.Text; vm.RenameBoard(); Close(); };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
            new TextBlock { Text = "Назва на цьому комп’ютері", FontSize = 20 }, input,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } }
        }};
    }
}
