using System;
using System.Windows.Forms;

namespace MiniOS
{
    // Точка входа всего проекта.
    internal static class Program
    {
        // Запускает WinForms-приложение и открывает рабочий стол MiniOS.
        [STAThread]
        private static void Main()
        {
            // Включает корректное масштабирование интерфейса на экранах с высоким DPI.
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            // Включает стандартные визуальные стили Windows для кнопок, списков и полей.
            Application.EnableVisualStyles();

            // Использует современный механизм отрисовки текста WinForms.
            Application.SetCompatibleTextRenderingDefault(false);

            // Передает управление главной форме MiniOS.
            Application.Run(new DesktopForm());
        }
    }
}
