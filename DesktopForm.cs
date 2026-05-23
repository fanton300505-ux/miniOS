using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MiniOS
{
    // Главное окно программы: рабочий стол, иконки запуска и центр управления мини-приложениями.
    internal sealed class DesktopForm : Form
    {
        // Генератор случайных значений для эмуляции нагрузки и фоновых процессов.
        private readonly Random random = new Random();

        // Общий список процессов, который читает диспетчер задач.
        private readonly List<MiniProcess> processes = new List<MiniProcess>();

        // Общий журнал событий, который показывает окно "Журнал событий".
        private readonly List<LogEntry> logs = new List<LogEntry>();

        // Виртуальная файловая система для проводника и текстового редактора.
        private readonly Dictionary<string, VirtualFolder> folders = new Dictionary<string, VirtualFolder>();

        // Каталог приложений, из которого создаются ярлыки на рабочем столе.
        private readonly List<AppDescriptor> apps = new List<AppDescriptor>();

        // Связь между открытым окном и его кнопкой на панели задач.
        private readonly Dictionary<MiniWindow, Button> taskButtons = new Dictionary<MiniWindow, Button>();

        // Связь между открытым окном и процессом в диспетчере задач.
        private readonly Dictionary<MiniWindow, MiniProcess> processByWindow = new Dictionary<MiniWindow, MiniProcess>();

        // Таймер, который раз в секунду обновляет часы и системные метрики.
        private readonly Timer systemTimer = new Timer();

        // Последние значения CPU, RAM, диска и сети.
        private readonly MetricSnapshot metrics = new MetricSnapshot();

        // Иконки запуска приложений, размещенные прямо на рабочем столе.
        private readonly List<Button> desktopAppIcons = new List<Button>();

        // Поверхность рабочего стола, на которой размещаются окна.
        private DesktopCanvas desktopSurface;

        // Всплывающее меню Пуск.
        private Panel startPanel;

        // Список приложений внутри меню Пуск.
        private FlowLayoutPanel startList;

        // Контейнер кнопок открытых приложений на панели задач.
        private FlowLayoutPanel taskButtonsPanel;

        // Часы в правой части панели задач.
        private Label clockLabel;

        // Текстовый индикатор загрузки CPU на панели задач.
        private Label cpuLabel;

        // Графический индикатор загрузки CPU на панели задач.
        private ProgressBar cpuProgress;

        // Строка состояния на рабочем столе.
        private Label desktopStatusLabel;

        // Следующий PID для создаваемого процесса.
        private int nextProcessId = 1000;

        // Смещение новых окон, чтобы они не открывались строго друг поверх друга.
        private int windowOffset;

        // Ширина зоны рабочего стола, где стоят иконки приложений.
        private const int DesktopIconAreaWidth = 140;

        // Событие для окон, которым нужно реагировать на изменение процессов или метрик.
        private event Action SystemPulse;

        // Событие для окна журнала, чтобы оно обновлялось при появлении новой записи.
        private event Action<LogEntry> LogAdded;

        // Создает главное окно MiniOS и запускает системный таймер.
        public DesktopForm()
        {
            Text = "MiniOS - учебная мини ОС";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 680);
            Size = new Size(1240, 760);
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(24, 29, 40);

            // Сначала создаются данные ОС, затем интерфейс, который эти данные показывает.
            InitializeVirtualFileSystem();
            InitializeProcesses();
            RegisterApplications();
            InitializeShell();
            BuildLaunchers();
            AddLog("Система", "MiniOS загружена. Рабочий стол готов.");

            // Главный системный тик обновляет часы, нагрузку и подписанные окна.
            systemTimer.Interval = 1000;
            systemTimer.Tick += delegate { UpdateSystemMetrics(); };
            systemTimer.Start();

            Resize += delegate { LayoutDesktopElements(); };

            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.UserPaint, true);
        }

        // Настраивает рабочий стол, панель задач, меню Пуск и системные индикаторы.
        private void InitializeShell()
        {
            // Рабочий стол занимает всю форму, а его фон рисуется в DesktopCanvas.
            desktopSurface = new DesktopCanvas
            {
                Dock = DockStyle.Fill
            };

            // Панель задач закреплена снизу и содержит Пуск, кнопки окон и системные индикаторы.
            var taskbar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.FromArgb(28, 34, 46),
                Padding = new Padding(10, 8, 10, 8)
            };

            Controls.Add(desktopSurface);
            Controls.Add(taskbar);

            // Название системы в правом верхнем углу рабочего стола.
            var title = new Label
            {
                Text = "MiniOS",
                AutoSize = false,
                Width = 360,
                Height = 58,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(ClientSize.Width - 390, 24),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Semibold", 28f, FontStyle.Bold)
            };

            // Короткая строка состояния показывает текущую нагрузку и число процессов.
            desktopStatusLabel = new Label
            {
                Text = "Готово",
                AutoSize = false,
                Width = 500,
                Height = 24,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                ForeColor = Color.FromArgb(210, 221, 232),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9.5f)
            };

            desktopSurface.Controls.Add(title);
            desktopSurface.Controls.Add(desktopStatusLabel);

            // Меню Пуск изначально скрыто и появляется над панелью задач.
            startPanel = new Panel
            {
                Size = new Size(330, 410),
                BackColor = Color.FromArgb(242, 245, 250),
                Visible = false,
                Padding = new Padding(12)
            };

            // Рамка меню Пуск рисуется вручную, чтобы панель выглядела как отдельное окно.
            startPanel.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(54, 63, 82)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, startPanel.Width - 1, startPanel.Height - 1);
                }
            };

            // Заголовок меню Пуск.
            var startHeader = new Label
            {
                Text = "Приложения",
                Dock = DockStyle.Top,
                Height = 42,
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(36, 43, 58),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Контейнер приложений в меню Пуск.
            startList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent
            };

            startPanel.Controls.Add(startList);
            startPanel.Controls.Add(startHeader);
            desktopSurface.Controls.Add(startPanel);

            // Кнопка Пуск показывает или скрывает список приложений.
            var startButton = CreateTaskbarButton("Пуск");
            startButton.Width = 104;
            startButton.Dock = DockStyle.Left;
            startButton.Click += delegate
            {
                startPanel.Visible = !startPanel.Visible;
                startPanel.BringToFront();
            };

            // Правая часть панели задач: CPU, прогресс-бар и часы.
            var statusPanel = new Panel
            {
                Width = 315,
                Dock = DockStyle.Right,
                BackColor = Color.Transparent
            };

            // Текстовая подпись текущей загрузки CPU.
            cpuLabel = new Label
            {
                Text = "CPU 0%",
                ForeColor = Color.White,
                Dock = DockStyle.Left,
                Width = 78,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
            };

            // Прогресс-бар визуально показывает эмулированную загрузку CPU.
            cpuProgress = new ProgressBar
            {
                Dock = DockStyle.Left,
                Width = 112,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 12, 10, 12)
            };

            // Часы обновляются системным таймером каждую секунду.
            clockLabel = new Label
            {
                Text = DateTime.Now.ToString("HH:mm:ss"),
                ForeColor = Color.FromArgb(226, 232, 240),
                Dock = DockStyle.Right,
                Width = 110,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold)
            };

            statusPanel.Controls.Add(clockLabel);
            statusPanel.Controls.Add(cpuProgress);
            statusPanel.Controls.Add(cpuLabel);

            // Центральная часть панели задач хранит кнопки открытых окон.
            taskButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 0, 8, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent
            };

            taskbar.Controls.Add(taskButtonsPanel);
            taskbar.Controls.Add(statusPanel);
            taskbar.Controls.Add(startButton);
            LayoutDesktopElements();
        }

        // Регистрирует приложения, доступные для запуска в MiniOS.
        private void RegisterApplications()
        {
            // Диспетчер задач управляет процессами и показывает CPU/RAM по каждому из них.
            apps.Add(new AppDescriptor("Диспетчер задач", "Диспетчер", "TM", Color.FromArgb(44, 123, 229), CreateTaskManagerWindow));

            // Проводник показывает виртуальные папки и файлы.
            apps.Add(new AppDescriptor("Проводник", "Файлы", "EX", Color.FromArgb(23, 151, 119), CreateExplorerWindow));

            // Журнал событий показывает историю действий пользователя и системы.
            apps.Add(new AppDescriptor("Журнал событий", "Журнал", "LOG", Color.FromArgb(207, 99, 42), CreateEventLogWindow));

            // Текстовый редактор открывает и сохраняет виртуальные текстовые файлы.
            apps.Add(new AppDescriptor("Текстовый редактор", "Редактор", "TXT", Color.FromArgb(122, 88, 214), delegate { return CreateTextEditorWindow(null); }));

            // Монитор системы показывает общие метрики и список служб.
            apps.Add(new AppDescriptor("Монитор системы", "Монитор", "SYS", Color.FromArgb(18, 135, 163), CreateSystemMonitorWindow));

            // Калькулятор демонстрирует отдельную прикладную программу.
            apps.Add(new AppDescriptor("Калькулятор", "Кальк", "=", Color.FromArgb(201, 62, 88), CreateCalculatorWindow));
        }

        // Создает ярлыки приложений прямо на рабочем столе.
        private void BuildLaunchers()
        {
            var index = 0;
            foreach (var app in apps)
            {
                var icon = (Button)CreateLauncherTile(app, false);
                icon.Location = GetDesktopIconLocation(index);
                desktopSurface.Controls.Add(icon);
                desktopAppIcons.Add(icon);
                startList.Controls.Add(CreateLauncherTile(app, true));
                index++;
            }
        }

        // Рассчитывает позицию иконки приложения на рабочем столе.
        private Point GetDesktopIconLocation(int index)
        {
            var row = index % 5;
            var column = index / 5;
            return new Point(22 + column * 104, 28 + row * 96);
        }

        // Создает кнопку-ярлык для запуска приложения.
        private Control CreateLauncherTile(AppDescriptor app, bool wide)
        {
            if (wide)
            {
                var startButton = new Button
                {
                    Width = 286,
                    Height = 52,
                    Text = app.Icon + "  " + app.Name,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 0, 0, 0),
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(33, 41, 54),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 0, 8)
                };

                startButton.FlatAppearance.BorderSize = 0;
                startButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 238, 247);
                startButton.FlatAppearance.MouseDownBackColor = app.Accent;
                startButton.Click += delegate { LaunchApplication(app); };
                return startButton;
            }

            // Иконка рабочего стола похожа на обычный ярлык: прозрачный фон, пиктограмма и подпись.
            var button = new DesktopIconButton(app.Icon, app.Accent, false)
            {
                Size = new Size(92, 82),
                Text = app.ShortName,
                Font = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold),
                AccessibleName = app.Name
            };

            button.Click += delegate { LaunchApplication(app); };
            return button;
        }

        // Запускает выбранное приложение по нажатию на иконку рабочего стола.
        private void LaunchApplication(AppDescriptor app)
        {
            OpenWindow(app.Factory(), app.Name, app.ShortName, app.Accent);
            if (startPanel != null)
            {
                startPanel.Visible = false;
            }
        }

        // Добавляет окно на рабочий стол и создает для него процесс и кнопку панели задач.
        private void OpenWindow(MiniWindow window, string processName, string taskText, Color accent)
        {
            // Окно добавляется на рабочий стол и сразу становится верхним.
            PositionWindow(window);
            desktopSurface.Controls.Add(window);
            window.BringToFront();
            startPanel.BringToFront();

            // Рабочий стол централизованно обрабатывает действия окна.
            window.ActivatedByUser += delegate { ActivateWindow(window); };
            window.MinimizeRequested += delegate { MinimizeWindow(window); };
            window.CloseRequested += delegate { CloseWindow(window, true); };

            // Для каждого окна создается отдельная запись в списке процессов.
            var process = new MiniProcess(
                nextProcessId++,
                processName,
                "User",
                random.Next(2, 18),
                random.Next(64, 260),
                window.WindowTitle);

            processes.Add(process);
            processByWindow[window] = process;

            // Кнопка на панели задач нужна для восстановления свернутого окна.
            var taskButton = CreateTaskbarButton(taskText);
            taskButton.Width = 132;
            taskButton.Tag = window;
            taskButton.Click += delegate { RestoreWindow(window); };
            taskButtonsPanel.Controls.Add(taskButton);
            taskButtons[window] = taskButton;

            // Лог и системный сигнал позволяют другим окнам сразу увидеть изменение.
            AddLog("Окно", "Запущено приложение «" + processName + "».");
            SystemPulse?.Invoke();
        }

        // Выбирает начальную позицию нового окна на рабочем столе.
        private void PositionWindow(MiniWindow window)
        {
            var x = DesktopIconAreaWidth + 26 + windowOffset * 30;
            var y = 28 + windowOffset * 24;
            windowOffset = (windowOffset + 1) % 8;

            x = Math.Min(x, Math.Max(DesktopIconAreaWidth + 12, desktopSurface.ClientSize.Width - window.Width - 12));
            y = Math.Min(y, Math.Max(12, desktopSurface.ClientSize.Height - window.Height - 12));
            window.Location = new Point(x, y);
        }

        // Делает окно активным и подсвечивает его кнопку на панели задач.
        private void ActivateWindow(MiniWindow window)
        {
            window.BringToFront();
            startPanel.BringToFront();

            foreach (var pair in taskButtons)
            {
                pair.Value.BackColor = pair.Key == window
                    ? Color.FromArgb(68, 82, 109)
                    : Color.FromArgb(45, 54, 72);
            }
        }

        // Сворачивает окно, оставляя его кнопку на панели задач.
        private void MinimizeWindow(MiniWindow window)
        {
            window.Visible = false;
            if (taskButtons.ContainsKey(window))
            {
                taskButtons[window].BackColor = Color.FromArgb(45, 54, 72);
            }

            AddLog("Окно", "Свернуто окно «" + window.WindowTitle + "».");
        }

        // Восстанавливает свернутое окно с панели задач.
        private void RestoreWindow(MiniWindow window)
        {
            if (window.IsDisposed)
            {
                return;
            }

            window.Visible = true;
            if (taskButtons.ContainsKey(window))
            {
                taskButtons[window].BackColor = Color.FromArgb(68, 82, 109);
            }

            ActivateWindow(window);
        }

        // Закрывает окно, останавливает или удаляет связанный процесс и убирает иконку восстановления.
        private void CloseWindow(MiniWindow window, bool removeProcess = false)
        {
            if (processByWindow.ContainsKey(window))
            {
                var process = processByWindow[window];
                process.State = ProcessState.Stopped;
                process.CpuUsage = 0;
                processByWindow.Remove(window);

                if (removeProcess)
                {
                    processes.Remove(process);
                }
            }

            if (taskButtons.ContainsKey(window))
            {
                var button = taskButtons[window];
                taskButtons.Remove(window);
                taskButtonsPanel.Controls.Remove(button);
                button.Dispose();
            }

            desktopSurface.Controls.Remove(window);
            AddLog("Окно", "Закрыто окно «" + window.WindowTitle + "».");
            window.Dispose();
            SystemPulse?.Invoke();
        }

        // Создает стандартную командную кнопку внутри приложений.
        private Button CreateCommandButton(string text, Color color)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = false,
                Width = 128,
                Height = 34,
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.18f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, 0.08f);
            return button;
        }

        // Создает кнопку для панели задач.
        private Button CreateTaskbarButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Height = 42,
                BackColor = Color.FromArgb(45, 54, 72),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(63, 76, 101);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(75, 93, 124);
            return button;
        }

        // Создает нижнюю строку состояния для окон приложений.
        private Label CreateStatusLabel()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(73, 82, 98),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
        }

        // Располагает системный виджет, статус и иконки при изменении размера окна.
        private void LayoutDesktopElements()
        {
            if (desktopSurface == null || desktopStatusLabel == null)
            {
                return;
            }

            desktopStatusLabel.Location = new Point(
                Math.Max(DesktopIconAreaWidth + 20, desktopSurface.ClientSize.Width - desktopStatusLabel.Width - 18),
                Math.Max(18, desktopSurface.ClientSize.Height - desktopStatusLabel.Height - 18));

            if (startPanel != null)
            {
                startPanel.Location = new Point(12, Math.Max(12, desktopSurface.ClientSize.Height - startPanel.Height - 12));
            }

            for (var i = 0; i < desktopAppIcons.Count; i++)
            {
                desktopAppIcons[i].Location = GetDesktopIconLocation(i);
            }
        }

        // Создает окно диспетчера задач со списком процессов и кнопками управления.
        private MiniWindow CreateTaskManagerWindow()
        {
            // Новый диспетчер задач начинает с чистого списка без старых остановленных процессов.
            RemoveStoppedProcesses();

            // Внутреннее окно приложения оформляется общим классом MiniWindow.
            var window = new MiniWindow("Диспетчер задач", "TM", Color.FromArgb(44, 123, 229), new Size(780, 500));

            // Основная сетка: сверху панель команд, в центре таблица, снизу статус.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // Панель команд диспетчера задач.
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var startButton = CreateCommandButton("Запуск", Color.FromArgb(23, 151, 119));
            var stopButton = CreateCommandButton("Остановить", Color.FromArgb(201, 62, 88));
            var closeProcessButton = CreateCommandButton("Закрыть процесс", Color.FromArgb(124, 45, 18));
            var refreshButton = CreateCommandButton("Обновить", Color.FromArgb(71, 85, 105));
            closeProcessButton.Width = 154;

            // Автообновление выключено по умолчанию, чтобы выделение процесса не сбивалось.
            var autoRefreshBox = new CheckBox
            {
                Text = "Автообновление",
                AutoSize = true,
                Checked = false,
                ForeColor = Color.FromArgb(73, 82, 98),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(8, 8, 0, 0)
            };
            toolbar.Controls.Add(startButton);
            toolbar.Controls.Add(stopButton);
            toolbar.Controls.Add(closeProcessButton);
            toolbar.Controls.Add(refreshButton);
            toolbar.Controls.Add(autoRefreshBox);

            // Таблица процессов имитирует список из настоящего диспетчера задач.
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            list.Columns.Add("PID", 70);
            list.Columns.Add("Процесс", 190);
            list.Columns.Add("Состояние", 110);
            list.Columns.Add("CPU", 70);
            list.Columns.Add("Память", 90);
            list.Columns.Add("Владелец", 90);
            list.Columns.Add("Окно", 130);

            // В статусе показаны общие числа и состояние автообновления.
            var status = CreateStatusLabel();

            // Обновляет список процессов, сохраняя выбранную строку.
            void RefreshProcesses()
            {
                if (list.IsDisposed)
                {
                    return;
                }

                var selectedProcessId = -1;
                if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is MiniProcess selectedProcess)
                {
                    selectedProcessId = selectedProcess.Id;
                }

                // Полная перерисовка таблицы проще, поэтому выбранный PID запоминается заранее.
                ListViewItem itemToSelect = null;
                list.BeginUpdate();
                list.Items.Clear();

                foreach (var process in processes.OrderBy(p => p.State).ThenBy(p => p.Id))
                {
                    var item = new ListViewItem(process.Id.ToString());
                    item.SubItems.Add(process.Name);
                    item.SubItems.Add(process.State == ProcessState.Running ? "Работает" : "Остановлен");
                    item.SubItems.Add(process.CpuUsage + "%");
                    item.SubItems.Add(process.MemoryMb + " МБ");
                    item.SubItems.Add(process.Owner);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(process.WindowTitle) ? "-" : process.WindowTitle);
                    item.Tag = process;

                    if (process.State == ProcessState.Stopped)
                    {
                        item.ForeColor = Color.FromArgb(132, 139, 152);
                    }

                    list.Items.Add(item);
                    if (process.Id == selectedProcessId)
                    {
                        itemToSelect = item;
                    }
                }

                list.EndUpdate();
                if (itemToSelect != null)
                {
                    itemToSelect.Selected = true;
                    itemToSelect.Focused = true;
                    itemToSelect.EnsureVisible();
                }

                var running = processes.Count(p => p.State == ProcessState.Running);
                status.Text = "Всего процессов: " + processes.Count + " • работает: " + running + " • CPU: " + metrics.Cpu + "% • авто: " + (autoRefreshBox.Checked ? "вкл" : "выкл");
            }

            startButton.Click += delegate
            {
                if (list.SelectedItems.Count == 0)
                {
                    AddLog("Процесс", "Запуск отменен: процесс не выбран.");
                    return;
                }

                // Запуск восстанавливает выбранный остановленный процесс.
                var process = (MiniProcess)list.SelectedItems[0].Tag;
                RestoreProcess(process);
                RefreshProcesses();
            };

            stopButton.Click += delegate
            {
                if (list.SelectedItems.Count == 0)
                {
                    AddLog("Процесс", "Остановка отменена: процесс не выбран.");
                    return;
                }

                var process = (MiniProcess)list.SelectedItems[0].Tag;

                // Остановка работает и для фоновых процессов, и для окон приложений.
                StopProcess(process);
                if (!list.IsDisposed)
                {
                    RefreshProcesses();
                }
            };

            closeProcessButton.Click += delegate
            {
                if (list.SelectedItems.Count == 0)
                {
                    AddLog("Процесс", "Закрытие отменено: процесс не выбран.");
                    return;
                }

                var process = (MiniProcess)list.SelectedItems[0].Tag;
                CloseProcess(process);
                if (!list.IsDisposed)
                {
                    RefreshProcesses();
                }
            };

            // Ручное обновление нужно, когда автообновление выключено.
            refreshButton.Click += delegate { RefreshProcesses(); };

            // Диспетчер задач слушает системный тик, но обновляет таблицу только по чекбоксу.
            Action pulseHandler = delegate
            {
                if (autoRefreshBox.Checked)
                {
                    RefreshProcesses();
                }
                else if (!status.IsDisposed)
                {
                    var running = processes.Count(p => p.State == ProcessState.Running);
                    status.Text = "Всего процессов: " + processes.Count + " • работает: " + running + " • CPU: " + metrics.Cpu + "% • авто: выкл";
                }
            };
            SystemPulse += pulseHandler;
            window.Disposed += delegate { SystemPulse -= pulseHandler; };

            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(list, 0, 1);
            layout.Controls.Add(status, 0, 2);
            window.Content.Controls.Add(layout);
            RefreshProcesses();
            return window;
        }

        // Создает окно проводника с деревом папок и списком виртуальных файлов.
        private MiniWindow CreateExplorerWindow()
        {
            // Окно проводника использует общий вид MiniWindow, но внутри строит файловый интерфейс.
            var window = new MiniWindow("Проводник", "EX", Color.FromArgb(23, 151, 119), new Size(790, 510));

            // Сетка окна: команды сверху, дерево и список файлов по центру, статус снизу.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // Панель команд проводника.
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var openButton = CreateCommandButton("Открыть", Color.FromArgb(44, 123, 229));
            var newButton = CreateCommandButton("Новый файл", Color.FromArgb(23, 151, 119));
            var newFolderButton = CreateCommandButton("Новая папка", Color.FromArgb(207, 99, 42));
            var deleteButton = CreateCommandButton("Удалить", Color.FromArgb(201, 62, 88));
            toolbar.Controls.Add(openButton);
            toolbar.Controls.Add(newButton);
            toolbar.Controls.Add(newFolderButton);
            toolbar.Controls.Add(deleteButton);

            // SplitContainer делит окно на левое дерево папок и правый список файлов.
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 210,
                BackColor = Color.FromArgb(226, 232, 240),
                FixedPanel = FixedPanel.Panel1
            };

            // Дерево папок показывает верхний уровень виртуальной файловой системы.
            var tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                HideSelection = false
            };

            // Список файлов показывает содержимое выбранной папки.
            var fileList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            fileList.Columns.Add("Имя", 230);
            fileList.Columns.Add("Тип", 110);
            fileList.Columns.Add("Размер", 90);
            fileList.Columns.Add("Изменен", 150);

            // Строка состояния показывает текущую папку и число элементов.
            var status = CreateStatusLabel();

            // Этот цикл оставлен как место для возможного расширения вложенных папок.
            foreach (var path in folders.Keys)
            {
                if (path == "Этот компьютер")
                {
                    continue;
                }
            }

            // Корневой узел проводника - "Этот компьютер".
            var root = new TreeNode("Этот компьютер") { Tag = "Этот компьютер" };

            // Рекурсивно добавляет вложенные папки в дерево проводника.
            void AddFolderNodes(TreeNode parentNode, string parentPath)
            {
                foreach (var folder in folders[parentPath].Items.Where(item => item.IsFolder))
                {
                    var folderPath = GetFolderPath(folder);
                    var node = new TreeNode(folder.Name) { Tag = folderPath };
                    parentNode.Nodes.Add(node);

                    if (folders.ContainsKey(folderPath))
                    {
                        AddFolderNodes(node, folderPath);
                    }
                }
            }

            // В дерево добавляются системные папки из виртуального корня.
            AddFolderNodes(root, "Этот компьютер");

            tree.Nodes.Add(root);
            root.Expand();
            tree.SelectedNode = root;

            // Показывает содержимое выбранной виртуальной папки.
            void RefreshFiles()
            {
                if (fileList.IsDisposed)
                {
                    return;
                }

                var path = tree.SelectedNode != null ? tree.SelectedNode.Tag.ToString() : "Этот компьютер";

                // Список полностью пересобирается из коллекции VirtualFolder.Items.
                fileList.BeginUpdate();
                fileList.Items.Clear();

                foreach (var item in folders[path].Items)
                {
                    var listItem = new ListViewItem(item.Name);
                    listItem.SubItems.Add(item.IsFolder ? "Папка" : item.Type);
                    listItem.SubItems.Add(item.IsFolder ? "-" : item.SizeKb + " КБ");
                    listItem.SubItems.Add(item.Modified.ToString("dd.MM.yyyy HH:mm"));
                    listItem.Tag = item;
                    if (item.IsFolder)
                    {
                        listItem.Font = new Font(fileList.Font, FontStyle.Bold);
                        listItem.ForeColor = Color.FromArgb(23, 151, 119);
                    }

                    fileList.Items.Add(listItem);
                }

                fileList.EndUpdate();
                status.Text = path + " • элементов: " + folders[path].Items.Count;
            }

            // Открывает выбранную папку или текстовый файл.
            void OpenSelected()
            {
                if (fileList.SelectedItems.Count == 0)
                {
                    return;
                }

                var item = (VirtualItem)fileList.SelectedItems[0].Tag;
                if (item.IsFolder)
                {
                    // Для папки меняем выбранный узел дерева.
                    var node = FindTreeNode(tree.Nodes, GetFolderPath(item));
                    if (node != null)
                    {
                        tree.SelectedNode = node;
                    }

                    return;
                }

                if (item.Type == "Текст")
                {
                    // Текстовые файлы открываются отдельным окном редактора.
                    OpenWindow(CreateTextEditorWindow(item), "Текстовый редактор", "TXT", Color.FromArgb(122, 88, 214));
                    AddLog("Файлы", "Открыт файл «" + item.Name + "» в редакторе.");
                }
                else
                {
                    MessageBox.Show("Мини-ОС умеет открывать только текстовые файлы.", "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    AddLog("Файлы", "Попытка открыть файл «" + item.Name + "».");
                }
            }

            tree.AfterSelect += delegate { RefreshFiles(); };
            fileList.DoubleClick += delegate { OpenSelected(); };
            openButton.Click += delegate { OpenSelected(); };

            newButton.Click += delegate
            {
                // В корне новый файл создается в папке "Документы".
                var currentPath = tree.SelectedNode != null ? tree.SelectedNode.Tag.ToString() : "Документы";
                if (currentPath == "Этот компьютер")
                {
                    currentPath = "Документы";
                }

                var name = ShowPrompt("Новый файл", "Введите имя текстового файла:", "Новый документ.txt");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    name += ".txt";
                }

                folders[currentPath].Items.Add(new VirtualItem(name, false, "Текст", 1, ""));
                AddLog("Файлы", "Создан файл «" + name + "» в папке «" + currentPath + "».");
                RefreshFiles();
            };

            newFolderButton.Click += delegate
            {
                // Новые папки создаются внутри выбранной папки, включая корень.
                var currentPath = tree.SelectedNode != null ? tree.SelectedNode.Tag.ToString() : "Этот компьютер";
                var name = ShowPrompt("Новая папка", "Введите имя папки:", "Новая папка");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (folders[currentPath].Items.Any(item => item.IsFolder && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Папка с таким именем уже существует.", "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var folderPath = BuildFolderPath(currentPath, name);
                folders[folderPath] = new VirtualFolder(folderPath);
                folders[currentPath].Items.Add(new VirtualItem(name, true, "Папка", 0, folderPath));

                var node = new TreeNode(name) { Tag = name };
                node.Tag = folderPath;
                var parentNode = tree.SelectedNode ?? root;
                parentNode.Nodes.Add(node);
                parentNode.Expand();
                tree.SelectedNode = node;

                AddLog("Файлы", "Создана папка «" + name + "» в папке «" + currentPath + "».");
                RefreshFiles();
            };

            deleteButton.Click += delegate
            {
                // Удаление работает только для выбранного элемента.
                if (fileList.SelectedItems.Count == 0)
                {
                    return;
                }

                var path = tree.SelectedNode != null ? tree.SelectedNode.Tag.ToString() : "Этот компьютер";
                var item = (VirtualItem)fileList.SelectedItems[0].Tag;

                // Системные папки в корне защищены от удаления.
                if (path == "Этот компьютер" && item.IsFolder && IsProtectedRootFolder(item.Name))
                {
                    MessageBox.Show("Системные папки нельзя удалить.", "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (item.IsFolder)
                {
                    var folderPath = GetFolderPath(item);
                    RemoveFolderRecursively(folderPath);
                    var node = FindTreeNode(tree.Nodes, folderPath);
                    if (node != null)
                    {
                        node.Remove();
                    }
                }

                folders[path].Items.Remove(item);
                AddLog("Файлы", "Удален элемент «" + item.Name + "» из папки «" + path + "».");
                RefreshFiles();
            };

            split.Panel1.Controls.Add(tree);
            split.Panel2.Controls.Add(fileList);
            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(split, 0, 1);
            layout.Controls.Add(status, 0, 2);
            window.Content.Controls.Add(layout);
            RefreshFiles();
            return window;
        }

        // Создает окно журнала событий с фильтром и очисткой записей.
        private MiniWindow CreateEventLogWindow()
        {
            // Окно журнала показывает все записи, которые добавляет метод AddLog.
            var window = new MiniWindow("Журнал событий", "LOG", Color.FromArgb(207, 99, 42), new Size(800, 500));

            // Сетка журнала: фильтр сверху, таблица событий в центре, статистика снизу.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // Панель фильтрации и очистки журнала.
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            // Выпадающий список ограничивает события выбранной категорией.
            var filter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170,
                Height = 32,
                Font = new Font("Segoe UI", 9.5f)
            };
            filter.Items.AddRange(new object[] { "Все", "Система", "Окно", "Процесс", "Файлы", "Редактор", "Калькулятор" });
            filter.SelectedIndex = 0;

            var clearButton = CreateCommandButton("Очистить", Color.FromArgb(201, 62, 88));
            toolbar.Controls.Add(filter);
            toolbar.Controls.Add(clearButton);

            // Таблица событий: время, категория и описание.
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            list.Columns.Add("Время", 105);
            list.Columns.Add("Категория", 110);
            list.Columns.Add("Сообщение", 520);

            // Строка состояния показывает число видимых записей.
            var status = CreateStatusLabel();

            // Обновляет список событий с учетом выбранного фильтра.
            void RefreshLog()
            {
                if (list.IsDisposed)
                {
                    return;
                }

                var selectedCategory = filter.SelectedItem.ToString();

                // Фильтр "Все" отключает ограничение по категории.
                var visibleLogs = logs
                    .Where(entry => selectedCategory == "Все" || entry.Category == selectedCategory)
                    .OrderByDescending(entry => entry.Time)
                    .ToList();

                list.BeginUpdate();
                list.Items.Clear();
                foreach (var entry in visibleLogs)
                {
                    var item = new ListViewItem(entry.Time.ToString("HH:mm:ss"));
                    item.SubItems.Add(entry.Category);
                    item.SubItems.Add(entry.Message);
                    list.Items.Add(item);
                }

                list.EndUpdate();
                status.Text = "Записей: " + visibleLogs.Count + " из " + logs.Count;
            }

            filter.SelectedIndexChanged += delegate { RefreshLog(); };
            clearButton.Click += delegate
            {
                // После очистки сразу добавляется новая системная запись о самом действии.
                logs.Clear();
                AddLog("Система", "Журнал событий очищен.");
                RefreshLog();
            };

            // Журнал подписывается на новые записи и отписывается при закрытии окна.
            Action<LogEntry> logHandler = delegate { RefreshLog(); };
            LogAdded += logHandler;
            window.Disposed += delegate { LogAdded -= logHandler; };

            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(list, 0, 1);
            layout.Controls.Add(status, 0, 2);
            window.Content.Controls.Add(layout);
            RefreshLog();
            return window;
        }

        // Создает текстовый редактор для нового или выбранного документа.
        private MiniWindow CreateTextEditorWindow(VirtualItem openedItem)
        {
            // Если openedItem передан из проводника, редактор сразу откроет этот файл.
            var window = new MiniWindow("Текстовый редактор", "TXT", Color.FromArgb(122, 88, 214), new Size(730, 500));

            // Сетка редактора: панель команд, большое поле ввода, строка состояния.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // Панель файловых команд редактора.
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var newButton = CreateCommandButton("Новый", Color.FromArgb(71, 85, 105));
            var openButton = CreateCommandButton("Открыть", Color.FromArgb(44, 123, 229));
            var saveButton = CreateCommandButton("Сохранить", Color.FromArgb(23, 151, 119));

            // Метка справа от кнопок показывает имя текущего файла.
            var currentLabel = new Label
            {
                AutoSize = false,
                Width = 240,
                Height = 34,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(73, 82, 98),
                Font = new Font("Segoe UI", 9f),
                AutoEllipsis = true
            };

            toolbar.Controls.Add(newButton);
            toolbar.Controls.Add(openButton);
            toolbar.Controls.Add(saveButton);
            toolbar.Controls.Add(currentLabel);

            // Основное поле редактирования текста.
            var editor = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                AcceptsTab = true,
                WordWrap = true,
                Font = new Font("Consolas", 11f),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // currentItem хранит файл, в который будет выполнено сохранение.
            var status = CreateStatusLabel();
            var currentItem = openedItem;

            // Обновляет счетчики символов, слов и имя текущего документа.
            void UpdateStatus()
            {
                var words = editor.Text
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Length;
                status.Text = "Символов: " + editor.Text.Length + " • слов: " + words;
                currentLabel.Text = currentItem == null ? "Без имени" : currentItem.Name;
            }

            // Загружает выбранный виртуальный текстовый файл в редактор.
            void LoadItem(VirtualItem item)
            {
                currentItem = item;
                editor.Text = item.Content;
                UpdateStatus();
                AddLog("Редактор", "Открыт документ «" + item.Name + "».");
            }

            if (openedItem != null)
            {
                // Редактор открыт из проводника для существующего файла.
                editor.Text = openedItem.Content;
            }
            else
            {
                // При обычном запуске создается новый несохраненный документ.
                editor.Text = "Новый документ MiniOS" + Environment.NewLine + Environment.NewLine;
            }

            newButton.Click += delegate
            {
                // Новый документ отвязывается от текущего файла до первого сохранения.
                currentItem = null;
                editor.Clear();
                editor.Focus();
                UpdateStatus();
                AddLog("Редактор", "Создан новый пустой документ.");
            };

            openButton.Click += delegate
            {
                // Открытие использует собственный диалог выбора виртуального файла.
                var selected = ShowTextFilePicker();
                if (selected != null)
                {
                    LoadItem(selected);
                }
            };

            saveButton.Click += delegate
            {
                if (currentItem == null)
                {
                    // Для нового файла сначала спрашиваем имя и сохраняем его в "Документы".
                    var name = ShowPrompt("Сохранить файл", "Введите имя файла:", "Документ.txt");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return;
                    }

                    if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        name += ".txt";
                    }

                    currentItem = new VirtualItem(name, false, "Текст", 1, editor.Text);
                    folders["Документы"].Items.Add(currentItem);
                }

                // Сохранение обновляет содержимое, размер и дату изменения.
                currentItem.Content = editor.Text;
                currentItem.SizeKb = Math.Max(1, editor.Text.Length / 1024 + 1);
                currentItem.Modified = DateTime.Now;
                UpdateStatus();
                AddLog("Редактор", "Сохранен документ «" + currentItem.Name + "».");
            };

            editor.TextChanged += delegate { UpdateStatus(); };

            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(editor, 0, 1);
            layout.Controls.Add(status, 0, 2);
            window.Content.Controls.Add(layout);
            UpdateStatus();
            return window;
        }

        // Создает окно монитора системы с метриками и списком служб.
        private MiniWindow CreateSystemMonitorWindow()
        {
            // Монитор системы показывает не отдельные процессы, а общую нагрузку MiniOS.
            var window = new MiniWindow("Монитор системы", "SYS", Color.FromArgb(18, 135, 163), new Size(680, 470));

            // Сетка монитора: метрики сверху, службы по центру, управление снизу.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            // Верхняя панель с четырьмя строками прогресса.
            var metricsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 3,
                Padding = new Padding(0, 0, 0, 8)
            };
            metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));

            // Каждая строка привязана к одной системной метрике.
            var cpuBar = CreateMetricRow(metricsPanel, 0, "CPU");
            var memBar = CreateMetricRow(metricsPanel, 1, "RAM");
            var diskBar = CreateMetricRow(metricsPanel, 2, "Диск");
            var netBar = CreateMetricRow(metricsPanel, 3, "Сеть");

            // Таблица служб имитирует простое управление системными службами.
            var services = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            services.Columns.Add("Служба", 220);
            services.Columns.Add("Состояние", 120);
            services.Columns.Add("Описание", 260);
            AddService(services, "FileService", "Работает", "виртуальная файловая система");
            AddService(services, "EventLogger", "Работает", "запись действий пользователя");
            AddService(services, "Updater", "Остановлена", "учебная проверка обновлений");
            AddService(services, "NetStack", "Работает", "эмуляция сетевой активности");

            // Нижняя панель позволяет переключать состояние выбранной службы.
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            var toggleButton = CreateCommandButton("Переключить", Color.FromArgb(44, 123, 229));
            var status = CreateStatusLabel();
            status.Width = 440;
            status.Dock = DockStyle.None;
            bottom.Controls.Add(toggleButton);
            bottom.Controls.Add(status);

            // Обновляет индикаторы CPU, RAM, диска и сети.
            void RefreshMetrics()
            {
                if (cpuBar.IsDisposed)
                {
                    return;
                }

                SetProgress(cpuBar, metrics.Cpu);
                SetProgress(memBar, metrics.Memory);
                SetProgress(diskBar, metrics.Disk);
                SetProgress(netBar, metrics.Network);
                status.Text = "Обновлено: " + metrics.Time.ToString("HH:mm:ss") + " • процессов активно: " + processes.Count(p => p.State == ProcessState.Running);
            }

            toggleButton.Click += delegate
            {
                // Переключение меняет только учебное состояние службы в таблице.
                if (services.SelectedItems.Count == 0)
                {
                    return;
                }

                var item = services.SelectedItems[0];
                item.SubItems[1].Text = item.SubItems[1].Text == "Работает" ? "Остановлена" : "Работает";
                item.ForeColor = item.SubItems[1].Text == "Работает" ? Color.FromArgb(23, 151, 119) : Color.FromArgb(201, 62, 88);
                AddLog("Система", "Служба " + item.Text + ": " + item.SubItems[1].Text.ToLower() + ".");
            };

            // Монитор подписывается на системный тик и обновляет прогресс-бары.
            Action pulseHandler = RefreshMetrics;
            SystemPulse += pulseHandler;
            window.Disposed += delegate { SystemPulse -= pulseHandler; };

            layout.Controls.Add(metricsPanel, 0, 0);
            layout.Controls.Add(services, 0, 1);
            layout.Controls.Add(bottom, 0, 2);
            window.Content.Controls.Add(layout);
            RefreshMetrics();
            return window;
        }

        // Создает окно калькулятора и его кнопочную сетку.
        private MiniWindow CreateCalculatorWindow()
        {
            // Калькулятор хранит выражение прямо в поле display.
            var window = new MiniWindow("Калькулятор", "=", Color.FromArgb(201, 62, 88), new Size(370, 500));

            // Сетка калькулятора: дисплей сверху, кнопки снизу.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Дисплей только показывает выражение и результат, ввод идет через кнопки.
            var display = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Right,
                Font = new Font("Segoe UI Semibold", 24f, FontStyle.Bold),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "0"
            };

            // Таблица 5 на 4 формирует ровную сетку кнопок.
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 4
            };

            for (var row = 0; row < 5; row++)
            {
                // Все строки получают одинаковую высоту.
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            }

            for (var column = 0; column < 4; column++)
            {
                // Все столбцы получают одинаковую ширину.
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            // Набор кнопок задает порядок их размещения в сетке.
            var buttons = new[]
            {
                "C", "⌫", "/", "*",
                "7", "8", "9", "-",
                "4", "5", "6", "+",
                "1", "2", "3", "=",
                "0", ".", "(", ")"
            };

            foreach (var text in buttons)
            {
                // Операторы подсвечиваются отдельным цветом.
                var button = new Button
                {
                    Dock = DockStyle.Fill,
                    Text = text,
                    Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = IsOperator(text) ? Color.FromArgb(245, 219, 224) : Color.White,
                    ForeColor = Color.FromArgb(34, 40, 53),
                    Margin = new Padding(4),
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(220, 226, 236);
                button.Click += delegate { HandleCalculatorInput(display, text); };
                grid.Controls.Add(button);
            }

            layout.Controls.Add(display, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            window.Content.Controls.Add(layout);
            return window;
        }

        // Создает строку системной метрики с подписью, прогрессом и процентом.
        private ProgressBar CreateMetricRow(TableLayoutPanel panel, int row, string caption)
        {
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(47, 55, 71),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };

            var bar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 9, 10, 9)
            };

            var value = new Label
            {
                Text = "0%",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(47, 55, 71),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Tag = bar
            };

            bar.Tag = value;
            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(bar, 1, row);
            panel.Controls.Add(value, 2, row);
            return bar;
        }

        // Устанавливает значение прогресс-бара и его текстового процента.
        private void SetProgress(ProgressBar bar, int value)
        {
            value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, value));
            bar.Value = value;
            if (bar.Tag is Label label)
            {
                label.Text = value + "%";
            }
        }

        // Проверяет, является ли кнопка калькулятора оператором.
        private static bool IsOperator(string text)
        {
            return text == "+" || text == "-" || text == "*" || text == "/" || text == "=" || text == "C" || text == "⌫";
        }

        // Обрабатывает нажатия кнопок калькулятора.
        private void HandleCalculatorInput(TextBox display, string text)
        {
            if (text == "C")
            {
                // C полностью очищает выражение.
                display.Text = "0";
                return;
            }

            if (text == "⌫")
            {
                // Backspace удаляет последний символ, но не оставляет пустой дисплей.
                display.Text = display.Text.Length <= 1 ? "0" : display.Text.Substring(0, display.Text.Length - 1);
                return;
            }

            if (text == "=")
            {
                try
                {
                    // DataTable.Compute используется как простой встроенный вычислитель выражений.
                    var expression = display.Text.Replace(',', '.');
                    var result = new DataTable().Compute(expression, null);
                    display.Text = Convert.ToString(result);
                    AddLog("Калькулятор", "Вычислено выражение: " + expression + " = " + display.Text + ".");
                }
                catch
                {
                    display.Text = "Ошибка";
                    AddLog("Калькулятор", "Ошибка вычисления выражения.");
                }

                return;
            }

            if (display.Text == "0" || display.Text == "Ошибка")
            {
                display.Text = text;
            }
            else
            {
                display.Text += text;
            }
        }

        // Добавляет строку службы в монитор системы.
        private void AddService(ListView services, string name, string state, string description)
        {
            // Цвет строки сразу показывает, работает служба или остановлена.
            var item = new ListViewItem(name);
            item.SubItems.Add(state);
            item.SubItems.Add(description);
            item.ForeColor = state == "Работает" ? Color.FromArgb(23, 151, 119) : Color.FromArgb(201, 62, 88);
            services.Items.Add(item);
        }

        // Возвращает остановленный процесс в состояние "Работает".
        private void RestoreProcess(MiniProcess process)
        {
            if (process.State == ProcessState.Running)
            {
                AddLog("Процесс", "Процесс «" + process.Name + "» уже работает.");
                return;
            }

            process.State = ProcessState.Running;
            process.CpuUsage = random.Next(2, 18);
            process.MemoryMb = Math.Max(process.MemoryMb, random.Next(32, 180));
            process.StartedAt = DateTime.Now;
            AddLog("Процесс", "Восстановлен процесс «" + process.Name + "».");
            SystemPulse?.Invoke();
        }

        // Останавливает выбранный процесс и закрывает связанное окно, если оно есть.
        private void StopProcess(MiniProcess process)
        {
            if (process.State == ProcessState.Stopped)
            {
                AddLog("Процесс", "Процесс «" + process.Name + "» уже остановлен.");
                return;
            }

            var window = processByWindow.FirstOrDefault(pair => pair.Value == process).Key;
            if (window != null)
            {
                // Если процесс принадлежит открытому окну, закрываем само окно.
                CloseWindow(window);
            }
            else
            {
                // Фоновый процесс просто переводится в состояние "Остановлен".
                process.State = ProcessState.Stopped;
                process.CpuUsage = 0;
            }

            AddLog("Процесс", "Остановлен процесс «" + process.Name + "».");
            SystemPulse?.Invoke();
        }

        // Полностью удаляет процесс из списка диспетчера задач.
        private void CloseProcess(MiniProcess process)
        {
            var window = processByWindow.FirstOrDefault(pair => pair.Value == process).Key;
            if (window != null)
            {
                // Для оконного процесса сначала закрывается окно, затем запись удаляется из списка.
                CloseWindow(window, true);
            }
            else
            {
                processes.Remove(process);
            }

            AddLog("Процесс", "Закрыт процесс «" + process.Name + "».");
            SystemPulse?.Invoke();
        }

        // Удаляет остановленные процессы перед открытием нового диспетчера задач.
        private void RemoveStoppedProcesses()
        {
            processes.RemoveAll(process => process.State == ProcessState.Stopped);
        }

        // Раз в секунду эмулирует системную нагрузку и обновляет индикаторы.
        private void UpdateSystemMetrics()
        {
            // Сначала немного меняется нагрузка каждого активного процесса.
            foreach (var process in processes.Where(p => p.State == ProcessState.Running))
            {
                process.CpuUsage = Math.Max(0, Math.Min(98, process.CpuUsage + random.Next(-8, 9)));
                process.MemoryMb = Math.Max(18, process.MemoryMb + random.Next(-6, 8));
            }

            var running = processes.Where(p => p.State == ProcessState.Running).ToList();

            // Общая CPU-нагрузка считается из среднего значения процессов с небольшим шумом.
            var cpuBase = running.Count == 0 ? 0 : Math.Min(100, running.Sum(p => p.CpuUsage) / Math.Max(1, running.Count));
            metrics.Cpu = Math.Max(3, Math.Min(100, cpuBase + random.Next(-5, 12)));
            metrics.Memory = Math.Max(12, Math.Min(95, running.Sum(p => p.MemoryMb) / 42 + random.Next(-3, 4)));
            metrics.Disk = Math.Max(1, Math.Min(100, metrics.Disk + random.Next(-12, 16)));
            metrics.Network = Math.Max(0, Math.Min(100, metrics.Network + random.Next(-18, 22)));
            metrics.Time = DateTime.Now;

            // Обновление панели задач и строки состояния рабочего стола.
            cpuProgress.Value = metrics.Cpu;
            cpuLabel.Text = "CPU " + metrics.Cpu + "%";
            clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
            desktopStatusLabel.Text = "CPU " + metrics.Cpu + "% • RAM " + metrics.Memory + "% • процессов " + running.Count;

            // Оповещаем открытые окна, которые подписались на системные изменения.
            SystemPulse?.Invoke();
        }

        // Добавляет запись в журнал событий.
        private void AddLog(string category, string message)
        {
            var entry = new LogEntry(category, message);
            logs.Add(entry);
            if (logs.Count > 500)
            {
                // Ограничение не дает журналу бесконечно расти во время работы программы.
                logs.RemoveAt(0);
            }

            // Окно журнала получает сигнал и может сразу обновить таблицу.
            LogAdded?.Invoke(entry);
        }

        // Показывает диалог ввода короткого текста.
        private string ShowPrompt(string title, string message, string defaultText)
        {
            // Диалог создается программно, без отдельной формы в дизайнере.
            using (var dialog = new Form())
            using (var input = new TextBox())
            using (var okButton = new Button())
            using (var cancelButton = new Button())
            using (var label = new Label())
            {
                dialog.Text = title;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(420, 170);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.BackColor = Color.FromArgb(246, 248, 252);

                // Подпись объясняет, какое значение нужно ввести.
                label.Text = message;
                label.Location = new Point(16, 16);
                label.Size = new Size(370, 24);

                input.Text = defaultText;
                input.Location = new Point(16, 46);
                input.Size = new Size(370, 28);

                // OK возвращает введенный текст.
                okButton.Text = "OK";
                okButton.DialogResult = DialogResult.OK;
                okButton.Location = new Point(214, 88);
                okButton.Size = new Size(82, 30);

                // Отмена закрывает диалог без результата.
                cancelButton.Text = "Отмена";
                cancelButton.DialogResult = DialogResult.Cancel;
                cancelButton.Location = new Point(304, 88);
                cancelButton.Size = new Size(82, 30);

                dialog.Controls.Add(label);
                dialog.Controls.Add(input);
                dialog.Controls.Add(okButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }

        // Показывает диалог выбора текстового файла из виртуальной файловой системы.
        private VirtualItem ShowTextFilePicker()
        {
            // Собираем все текстовые файлы из всех виртуальных папок.
            var textFiles = folders.Values
                .SelectMany(folder => folder.Items)
                .Where(item => !item.IsFolder && item.Type == "Текст")
                .OrderBy(item => item.Name)
                .ToList();

            using (var dialog = new Form())
            using (var list = new ListBox())
            using (var okButton = new Button())
            using (var cancelButton = new Button())
            {
                // Отдельный компактный диалог заменяет настоящий OpenFileDialog.
                dialog.Text = "Открыть документ";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(420, 360);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.BackColor = Color.FromArgb(246, 248, 252);

                list.Location = new Point(14, 14);
                list.Size = new Size(374, 250);

                // ListBox показывает свойство Name каждого VirtualItem.
                list.DisplayMember = "Name";
                list.DataSource = textFiles;

                okButton.Text = "Открыть";
                okButton.DialogResult = DialogResult.OK;
                okButton.Location = new Point(206, 278);
                okButton.Size = new Size(88, 30);

                // При отмене метод вернет null.
                cancelButton.Text = "Отмена";
                cancelButton.DialogResult = DialogResult.Cancel;
                cancelButton.Location = new Point(302, 278);
                cancelButton.Size = new Size(88, 30);

                dialog.Controls.Add(list);
                dialog.Controls.Add(okButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedItem is VirtualItem selected)
                {
                    return selected;
                }

                return null;
            }
        }

        // Ищет узел дерева проводника по виртуальному пути.
        private TreeNode FindTreeNode(TreeNodeCollection nodes, string path)
        {
            // Рекурсивно проверяем текущий уровень и все дочерние узлы.
            foreach (TreeNode node in nodes)
            {
                if (node.Tag != null && node.Tag.ToString() == path)
                {
                    return node;
                }

                var nested = FindTreeNode(node.Nodes, path);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        // Возвращает внутренний путь папки из элемента проводника.
        private static string GetFolderPath(VirtualItem item)
        {
            return string.IsNullOrWhiteSpace(item.Content) ? item.Name : item.Content;
        }

        // Строит путь новой папки относительно родителя.
        private static string BuildFolderPath(string parentPath, string folderName)
        {
            return parentPath == "Этот компьютер" ? folderName : parentPath + "\\" + folderName;
        }

        // Удаляет папку и все вложенные папки из виртуальной файловой системы.
        private void RemoveFolderRecursively(string folderPath)
        {
            if (!folders.ContainsKey(folderPath))
            {
                return;
            }

            foreach (var nestedFolder in folders[folderPath].Items.Where(item => item.IsFolder).ToList())
            {
                RemoveFolderRecursively(GetFolderPath(nestedFolder));
            }

            folders.Remove(folderPath);
        }

        // Проверяет, относится ли папка в корне к системным папкам MiniOS.
        private static bool IsProtectedRootFolder(string folderName)
        {
            return folderName == "Документы"
                || folderName == "Система"
                || folderName == "Изображения"
                || folderName == "Корзина";
        }

        // Заполняет виртуальную файловую систему начальными папками и файлами.
        private void InitializeVirtualFileSystem()
        {
            // Сначала создаются папки, чтобы затем можно было добавить в них элементы.
            folders["Этот компьютер"] = new VirtualFolder("Этот компьютер");
            folders["Документы"] = new VirtualFolder("Документы");
            folders["Система"] = new VirtualFolder("Система");
            folders["Изображения"] = new VirtualFolder("Изображения");
            folders["Корзина"] = new VirtualFolder("Корзина");

            // Корень проводника содержит ссылки на основные папки.
            folders["Этот компьютер"].Items.Add(new VirtualItem("Документы", true, "Папка", 0, "Документы"));
            folders["Этот компьютер"].Items.Add(new VirtualItem("Система", true, "Папка", 0, "Система"));
            folders["Этот компьютер"].Items.Add(new VirtualItem("Изображения", true, "Папка", 0, "Изображения"));
            folders["Этот компьютер"].Items.Add(new VirtualItem("Корзина", true, "Папка", 0, "Корзина"));

            // Начальные файлы нужны, чтобы проводник и редактор были не пустыми при запуске.
            folders["Документы"].Items.Add(new VirtualItem("Заметка.txt", false, "Текст", 2, "Добро пожаловать в MiniOS!" + Environment.NewLine));
            folders["Документы"].Items.Add(new VirtualItem("План проекта.txt", false, "Текст", 3, "1. Рабочий стол" + Environment.NewLine + "2. Окна приложений" + Environment.NewLine + "3. Диспетчер задач" + Environment.NewLine));
            folders["Система"].Items.Add(new VirtualItem("kernel.log", false, "Текст", 5, "MiniOS kernel boot OK" + Environment.NewLine));
            folders["Система"].Items.Add(new VirtualItem("services.cfg", false, "Текст", 1, "FileService=on" + Environment.NewLine + "EventLogger=on" + Environment.NewLine));
            folders["Изображения"].Items.Add(new VirtualItem("wallpaper.mini", false, "Файл", 128, ""));
            folders["Корзина"].Items.Add(new VirtualItem("old-note.txt", false, "Текст", 1, "Удаленная заметка" + Environment.NewLine));
        }

        // Создает начальные системные процессы и базовые метрики.
        private void InitializeProcesses()
        {
            // Эти процессы имитируют базовые системные службы после загрузки MiniOS.
            processes.Add(new MiniProcess(nextProcessId++, "DesktopShell", "System", 8, 180, "Рабочий стол"));
            processes.Add(new MiniProcess(nextProcessId++, "EventLogger", "System", 3, 72, ""));
            processes.Add(new MiniProcess(nextProcessId++, "FileService", "System", 5, 96, ""));
            processes.Add(new MiniProcess(nextProcessId++, "SystemClock", "System", 1, 28, ""));

            // Начальные метрики дают монитору системы стартовые значения.
            metrics.Cpu = 12;
            metrics.Memory = 28;
            metrics.Disk = 8;
            metrics.Network = 3;
            metrics.Time = DateTime.Now;
        }
    }

    // Кнопка-иконка рабочего стола: прозрачный фон, пиктограмма и подпись.
    internal sealed class DesktopIconButton : Button
    {
        private readonly string iconKey;
        private readonly Color accentColor;
        private readonly bool compact;

        private bool mouseOver;
        private bool mouseDown;

        // Переопределяем обработку сообщений, чтобы полностью заблокировать стирание фона.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x14) // WM_ERASEBKGND
            {
                m.Result = (IntPtr)1; // сообщаем, что фон стёрт (но мы ничего не делаем)
                return;
            }
            base.WndProc(ref m);
        }

        public DesktopIconButton(string iconKey, Color accentColor, bool compact)
        {
            this.iconKey = iconKey;
            this.accentColor = accentColor;
            this.compact = compact;

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;   // Прозрачный фон
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            TabStop = false;

            // Включаем двойную буферизацию и поддержку прозрачности
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        // Отключаем закраску фона — теперь фон будет браться от родителя
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Ничего не делаем, чтобы не закрашивать область чёрным
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            mouseOver = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            mouseOver = false;
            mouseDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            mouseDown = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            mouseDown = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Подсветка при наведении/нажатии (полупрозрачная заливка поверх фона)
            if (mouseOver || mouseDown)
            {
                var selection = new Rectangle(2, 2, Width - 5, Height - 5);
                using (var path = CreateRoundedPath(selection, 8))
                using (var fill = new SolidBrush(Color.FromArgb(mouseDown ? 90 : 56, 255, 255, 255)))
                using (var border = new Pen(Color.FromArgb(110, 255, 255, 255)))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
            }

            // Иконка
            int iconSize = compact ? 34 : 44;
            var iconRect = new Rectangle((Width - iconSize) / 2, 7, iconSize, iconSize);
            DrawApplicationIcon(e.Graphics, iconRect);

            // Текст под иконкой
            using (var labelBrush = new SolidBrush(Color.White))
            using (var labelFont = new Font("Segoe UI Semibold", compact ? 8f : 8.5f, FontStyle.Bold))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                int labelTop = iconRect.Bottom + 5;
                var labelRect = new Rectangle(6, labelTop, Width - 12, Height - labelTop - 6);
                e.Graphics.DrawString(Text, labelFont, labelBrush, labelRect, format);
            }
        }

        // Рисует простую Windows-подобную пиктограмму без внешних файлов.
        private void DrawApplicationIcon(Graphics graphics, Rectangle rect)
        {
            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            using (var accentBrush = new SolidBrush(accentColor))
            using (var lightBrush = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (var darkPen = new Pen(Color.FromArgb(90, 31, 41, 55), 1.2f))
            using (var whitePen = new Pen(Color.White, 2f))
            {
                graphics.FillEllipse(shadow, rect.Left + 3, rect.Top + 4, rect.Width - 2, rect.Height - 2);

                if (iconKey == "EX")
                {
                    var tab = new Rectangle(rect.Left + 5, rect.Top + 8, 17, 8);
                    var folder = new Rectangle(rect.Left + 3, rect.Top + 14, rect.Width - 6, rect.Height - 15);
                    using (var tabBrush = new SolidBrush(Color.FromArgb(244, 190, 64)))
                    using (var folderBrush = new SolidBrush(Color.FromArgb(250, 208, 87)))
                    {
                        graphics.FillRectangle(tabBrush, tab);
                        graphics.FillRectangle(folderBrush, folder);
                    }
                    graphics.DrawRectangle(darkPen, folder);
                    return;
                }

                if (iconKey == "TXT" || iconKey == "LOG")
                {
                    var page = new Rectangle(rect.Left + 9, rect.Top + 4, rect.Width - 16, rect.Height - 8);
                    graphics.FillRectangle(lightBrush, page);
                    graphics.DrawRectangle(darkPen, page);
                    using (var linePen = new Pen(iconKey == "LOG" ? Color.FromArgb(207, 99, 42) : Color.FromArgb(122, 88, 214), 2f))
                    {
                        for (var y = page.Top + 11; y < page.Bottom - 5; y += 8)
                        {
                            graphics.DrawLine(linePen, page.Left + 6, y, page.Right - 6, y);
                        }
                    }
                    return;
                }

                if (iconKey == "TM")
                {
                    graphics.FillEllipse(accentBrush, rect);
                    var baseY = rect.Bottom - 9;
                    var barWidth = 5;
                    for (var i = 0; i < 4; i++)
                    {
                        var height = 10 + i * 5;
                        graphics.FillRectangle(lightBrush, rect.Left + 10 + i * 8, baseY - height, barWidth, height);
                    }
                    return;
                }

                if (iconKey == "SYS")
                {
                    graphics.FillEllipse(accentBrush, rect);
                    graphics.DrawEllipse(whitePen, rect.Left + 11, rect.Top + 11, rect.Width - 22, rect.Height - 22);
                    graphics.DrawLine(whitePen, rect.Left + rect.Width / 2, rect.Top + 7, rect.Left + rect.Width / 2, rect.Top + 17);
                    graphics.DrawLine(whitePen, rect.Left + rect.Width / 2, rect.Bottom - 7, rect.Left + rect.Width / 2, rect.Bottom - 17);
                    graphics.DrawLine(whitePen, rect.Left + 7, rect.Top + rect.Height / 2, rect.Left + 17, rect.Top + rect.Height / 2);
                    graphics.DrawLine(whitePen, rect.Right - 7, rect.Top + rect.Height / 2, rect.Right - 17, rect.Top + rect.Height / 2);
                    return;
                }

                graphics.FillEllipse(accentBrush, rect);
                using (var font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString("=", font, lightBrush, rect, format);
                }
            }
        }

        // Создает геометрию со скругленными углами.
        private static GraphicsPath CreateRoundedPath(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Отдельная панель, которая рисует фон рабочего стола MiniOS.
    internal sealed class DesktopCanvas : Panel
    {
        // Настраивает поверхность рабочего стола для плавной перерисовки.
        public DesktopCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.Transparent; // Важно для поддержки прозрачности дочерних контролов
            // Включаем стили для поддержки прозрачных дочерних контролов и двойной буферизации
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        // Отключаем закраску фона, чтобы не затирать прозрачные области дочерних кнопок.
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Ничего не делаем – фон будет нарисован только в OnPaint
        }

        // Рисует фон рабочего стола.
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Основной градиент создает фон рабочего стола.
            using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(35, 49, 64), Color.FromArgb(39, 74, 76), 35f))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            // Диагональные линии добавляют декоративную сетку внизу.
            using (var pen = new Pen(Color.FromArgb(46, 190, 164), 2f))
            {
                for (var i = 0; i < Width; i += 130)
                {
                    e.Graphics.DrawLine(pen, i, Height, i + 120, Math.Max(0, Height - 72));
                }
            }

            // Две дополнительные линии делают фон менее пустым.
            using (var pen = new Pen(Color.FromArgb(120, 230, 180, 92), 1f))
            {
                e.Graphics.DrawLine(pen, 0, 120, Width, 56);
                e.Graphics.DrawLine(pen, 160, 0, Width, Height - 120);
            }
        }
    }
}