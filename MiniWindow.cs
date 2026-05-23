using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiniOS
{
    // Универсальное внутреннее окно MiniOS, которое живет внутри рабочего стола.
    internal sealed class MiniWindow : Panel
    {
        // Верхняя панель с названием приложения и кнопками управления.
        private readonly Panel titleBar;

        // Текст заголовка окна.
        private readonly Label titleLabel;

        // Кнопка сворачивания окна.
        private readonly Button minimizeButton;

        // Кнопка разворачивания и восстановления окна.
        private readonly Button maximizeButton;

        // Кнопка закрытия окна.
        private readonly Button closeButton;

        // Цвет окна, зависящий от запущенного приложения.
        private readonly Color accentColor;

        // Флаг показывает, что пользователь сейчас перетаскивает окно.
        private bool dragging;

        // Флаг показывает, что окно развернуто почти на весь рабочий стол.
        private bool maximized;

        // Смещение курсора относительно левого верхнего угла при перетаскивании.
        private Point dragOffset;

        // Старые размеры окна, чтобы вернуть их после восстановления.
        private Rectangle normalBounds;

        // Контейнер, в который конкретное приложение помещает свои элементы интерфейса.
        public Panel Content { get; }

        // Заголовок окна, используемый в логах и списке процессов.
        public string WindowTitle { get; }

        // Событие сообщает рабочему столу, что окно нужно свернуть.
        public event EventHandler MinimizeRequested;

        // Событие сообщает рабочему столу, что окно нужно закрыть.
        public event EventHandler CloseRequested;

        // Событие сообщает рабочему столу, что пользователь выбрал это окно.
        public event EventHandler ActivatedByUser;

        // Создает внутреннее окно приложения с заголовком и системными кнопками.
        public MiniWindow(string title, string icon, Color accent, Size size)
        {
            WindowTitle = title;
            accentColor = accent;
            Size = size;
            MinimumSize = new Size(360, 260);
            BackColor = Color.FromArgb(246, 248, 252);
            BorderStyle = BorderStyle.None;
            Padding = new Padding(1);
            DoubleBuffered = true;

            // Заголовок служит и декоративной полосой, и зоной перетаскивания.
            titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = accentColor,
                Cursor = Cursors.SizeAll
            };

            // Текстовая иконка заменяет изображения, чтобы проект оставался простым.
            var iconLabel = new Label
            {
                Text = icon,
                Dock = DockStyle.Left,
                Width = 42,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold)
            };

            // Название окна растягивается на свободное место в заголовке.
            titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                AutoEllipsis = true,
                Padding = new Padding(2, 0, 0, 1)
            };

            closeButton = CreateTitleButton("X");
            maximizeButton = CreateTitleButton("□");
            minimizeButton = CreateTitleButton("_");

            // Кнопки не закрывают окно сами, а отправляют события главному рабочему столу.
            closeButton.Click += delegate { CloseRequested?.Invoke(this, EventArgs.Empty); };
            minimizeButton.Click += delegate { MinimizeRequested?.Invoke(this, EventArgs.Empty); };
            maximizeButton.Click += delegate { ToggleMaximize(); };

            // Порядок добавления важен для DockStyle.Right: кнопки располагаются справа.
            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(iconLabel);
            titleBar.Controls.Add(closeButton);
            titleBar.Controls.Add(maximizeButton);
            titleBar.Controls.Add(minimizeButton);

            // Содержимое окна отделено от заголовка, чтобы приложения строили UI внутри Content.
            Content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 248, 252),
                Padding = new Padding(14)
            };

            Controls.Add(Content);
            Controls.Add(titleBar);

            // Обработчики мыши позволяют двигать окно за заголовок и иконку.
            titleBar.MouseDown += BeginDrag;
            titleLabel.MouseDown += BeginDrag;
            iconLabel.MouseDown += BeginDrag;
            titleBar.MouseMove += DragWindow;
            titleLabel.MouseMove += DragWindow;
            iconLabel.MouseMove += DragWindow;
            titleBar.MouseUp += EndDrag;
            titleLabel.MouseUp += EndDrag;
            iconLabel.MouseUp += EndDrag;

            // Любой клик по окну сообщает рабочему столу, что окно стало активным.
            MouseDown += delegate { ActivatedByUser?.Invoke(this, EventArgs.Empty); };
            titleBar.MouseDown += delegate { ActivatedByUser?.Invoke(this, EventArgs.Empty); };
            Content.MouseDown += delegate { ActivatedByUser?.Invoke(this, EventArgs.Empty); };
        }

        // Рисует рамку вокруг внутреннего окна.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var borderPen = new Pen(Color.FromArgb(46, 57, 77), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        // Переключает окно между обычным и развернутым состоянием.
        public void ToggleMaximize()
        {
            if (Parent == null)
            {
                return;
            }

            if (!maximized)
            {
                normalBounds = Bounds;
                Bounds = new Rectangle(8, 8, Parent.ClientSize.Width - 16, Parent.ClientSize.Height - 16);
                maximized = true;
                maximizeButton.Text = "❐";
            }
            else
            {
                Bounds = normalBounds;
                maximized = false;
                maximizeButton.Text = "□";
            }
        }

        // Создает кнопку в заголовке окна.
        private Button CreateTitleButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Right,
                Width = 44,
                FlatStyle = FlatStyle.Flat,
                BackColor = accentColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                TabStop = false,
                Cursor = Cursors.Hand
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(accentColor, 0.15f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(accentColor, 0.08f);
            return button;
        }

        // Начинает перетаскивание окна мышью.
        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || maximized)
            {
                return;
            }

            dragging = true;
            dragOffset = e.Location;
            BringToFront();
        }

        // Перемещает окно по рабочему столу во время перетаскивания.
        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (!dragging || Parent == null)
            {
                return;
            }

            var newLeft = Left + e.X - dragOffset.X;
            var newTop = Top + e.Y - dragOffset.Y;
            newLeft = Math.Max(4, Math.Min(newLeft, Parent.ClientSize.Width - Width - 4));
            newTop = Math.Max(4, Math.Min(newTop, Parent.ClientSize.Height - Height - 4));
            Location = new Point(newLeft, newTop);
        }

        // Завершает перетаскивание окна.
        private void EndDrag(object sender, MouseEventArgs e)
        {
            dragging = false;
        }
    }
}
