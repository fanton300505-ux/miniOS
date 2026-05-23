using System;
using System.Collections.Generic;
using System.Drawing;

namespace MiniOS
{
    // Состояние процесса, которое отображается в диспетчере задач.
    internal enum ProcessState
    {
        // Процесс считается активным и участвует в расчете нагрузки.
        Running,

        // Процесс остановлен, CPU для него сбрасывается в 0.
        Stopped
    }

    // Модель одного процесса в учебной операционной системе.
    internal sealed class MiniProcess
    {
        // Уникальный идентификатор процесса, похожий на PID в настоящей ОС.
        public int Id { get; set; }

        // Название процесса, которое показывается в диспетчере задач.
        public string Name { get; set; }

        // Владелец процесса: пользователь или система.
        public string Owner { get; set; }

        // Текущая эмулированная загрузка CPU в процентах.
        public int CpuUsage { get; set; }

        // Эмулированное потребление памяти в мегабайтах.
        public int MemoryMb { get; set; }

        // Время запуска процесса.
        public DateTime StartedAt { get; set; }

        // Текущее состояние процесса.
        public ProcessState State { get; set; }

        // Название окна, если процесс связан с открытым приложением.
        public string WindowTitle { get; set; }

        // Создает запись процесса для диспетчера задач.
        public MiniProcess(int id, string name, string owner, int cpuUsage, int memoryMb, string windowTitle)
        {
            Id = id;
            Name = name;
            Owner = owner;
            CpuUsage = cpuUsage;
            MemoryMb = memoryMb;
            StartedAt = DateTime.Now;
            State = ProcessState.Running;
            WindowTitle = windowTitle;
        }
    }

    // Модель одной строки в журнале событий.
    internal sealed class LogEntry
    {
        // Время создания записи.
        public DateTime Time { get; }

        // Категория записи: система, окно, процесс, файлы и так далее.
        public string Category { get; }

        // Текст события, который видит пользователь.
        public string Message { get; }

        // Создает запись журнала событий.
        public LogEntry(string category, string message)
        {
            Time = DateTime.Now;
            Category = category;
            Message = message;
        }
    }

    // Модель элемента виртуальной файловой системы.
    internal sealed class VirtualItem
    {
        // Имя файла или папки.
        public string Name { get; set; }

        // Определяет, является элемент папкой или обычным файлом.
        public bool IsFolder { get; set; }

        // Тип элемента, который отображается в проводнике.
        public string Type { get; set; }

        // Размер виртуального файла в килобайтах.
        public int SizeKb { get; set; }

        // Время последнего изменения элемента.
        public DateTime Modified { get; set; }

        // Содержимое текстового файла, с которым работает редактор.
        public string Content { get; set; }

        // Создает виртуальный файл или папку для проводника.
        public VirtualItem(string name, bool isFolder, string type, int sizeKb, string content)
        {
            Name = name;
            IsFolder = isFolder;
            Type = type;
            SizeKb = sizeKb;
            Content = content;
            Modified = DateTime.Now;
        }
    }

    // Описание приложения, из которого создаются ярлыки и окна.
    internal sealed class AppDescriptor
    {
        // Полное название приложения.
        public string Name { get; }

        // Короткое название для компактной кнопки на рабочем столе.
        public string ShortName { get; }

        // Текстовая иконка, показанная в ярлыке и заголовке окна.
        public string Icon { get; }

        // Цвет приложения для заголовка окна и акцентов интерфейса.
        public Color Accent { get; }

        // Фабрика, которая создает окно приложения при запуске.
        public Func<MiniWindow> Factory { get; }

        // Описывает приложение, доступное на рабочем столе и в меню Пуск.
        public AppDescriptor(string name, string shortName, string icon, Color accent, Func<MiniWindow> factory)
        {
            Name = name;
            ShortName = shortName;
            Icon = icon;
            Accent = accent;
            Factory = factory;
        }
    }

    // Текущий снимок эмулированных системных метрик.
    internal sealed class MetricSnapshot
    {
        // Загрузка процессора в процентах.
        public int Cpu { get; set; }

        // Использование памяти в процентах.
        public int Memory { get; set; }

        // Активность диска в процентах.
        public int Disk { get; set; }

        // Сетевая активность в процентах.
        public int Network { get; set; }

        // Время последнего обновления метрик.
        public DateTime Time { get; set; }
    }

    // Виртуальная папка, содержащая набор файлов и подпапок.
    internal sealed class VirtualFolder
    {
        // Путь или имя папки в виртуальной файловой системе.
        public string Path { get; }

        // Элементы, которые находятся внутри папки.
        public List<VirtualItem> Items { get; }

        // Создает виртуальную папку с указанным путем.
        public VirtualFolder(string path)
        {
            Path = path;
            Items = new List<VirtualItem>();
        }
    }
}
