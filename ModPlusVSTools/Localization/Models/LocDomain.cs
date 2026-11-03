using System;
using System.Collections.ObjectModel;

namespace ModPlusVSTools.Localization.Models
{
    /// <summary>
    /// Группа узлов локализации по префиксу имени файла: Common, AutoCAD, Revit, Renga, nanoCAD.
    /// </summary>
    internal sealed class LocDomain : ObservableBase
    {
        private bool _isExpanded;

        /// <summary>
        /// Создаёт группу.
        /// </summary>
        /// <param name="name">Имя группы.</param>
        public LocDomain(string name)
        {
            Name = name;
            IsCommon = string.Equals(name, Services.LocalizationFolder.CommonDomainName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Имя группы.</summary>
        public string Name { get; }

        /// <summary>Общая группа (файл Common.xml).</summary>
        public bool IsCommon { get; }

        /// <summary>Узлы группы (по алфавиту).</summary>
        public ObservableCollection<LocModule> Modules { get; } = new ObservableCollection<LocModule>();

        /// <summary>Развёрнут ли узел дерева.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetField(ref _isExpanded, value);
        }

        /// <summary>
        /// Вставляет модуль с сохранением алфавитного порядка.
        /// </summary>
        /// <param name="module">Модуль.</param>
        public void InsertSorted(LocModule module)
        {
            var index = 0;
            while (index < Modules.Count &&
                   string.Compare(Modules[index].Name, module.Name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                index++;
            }

            Modules.Insert(index, module);
        }
    }
}
