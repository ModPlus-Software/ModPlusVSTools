using System;
using System.Threading;
using System.Windows;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using ModPlusVSTools.Localization.ViewModels;

namespace ModPlusVSTools.Localization
{
    /// <summary>
    /// Сообщения и вопросы в стиле Visual Studio.
    /// </summary>
    internal static class Ui
    {
        private const string Caption = "ModPlus — локализация";
        private const int IdYes = 6;
        private const int IdNo = 7;

        /// <summary>
        /// Показывает сообщение.
        /// </summary>
        public static void ShowMessage(string message, OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                message,
                Caption,
                icon,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>
        /// Задаёт вопрос «Да/Нет».
        /// </summary>
        /// <returns><c>true</c> — «Да».</returns>
        public static bool AskYesNo(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                message,
                Caption,
                OLEMSGICON.OLEMSGICON_QUERY,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == IdYes;
        }

        /// <summary>
        /// Задаёт вопрос «Да/Нет/Отмена».
        /// </summary>
        /// <returns><c>true</c> — «Да», <c>false</c> — «Нет», <c>null</c> — «Отмена».</returns>
        public static bool? AskYesNoCancel(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                message,
                Caption,
                OLEMSGICON.OLEMSGICON_QUERY,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            return result switch
            {
                IdYes => true,
                IdNo => false,
                _ => (bool?)null,
            };
        }

        /// <summary>
        /// Помещает текст в буфер обмена (с повторами: буфер может быть временно занят другим процессом).
        /// </summary>
        public static void SetClipboardText(string text)
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(10);
                }
            }
        }

        /// <summary>
        /// Читает текст из буфера обмена.
        /// </summary>
        public static string GetClipboardText()
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                }
                catch (Exception)
                {
                    Thread.Sleep(10);
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Текущий экземпляр редактора локализации (для команд меню и закрытия VS).
    /// </summary>
    internal static class LocalizationEditorSession
    {
        /// <summary>Модель редактора или <c>null</c>, если редактор не открывался.</summary>
        public static EditorViewModel? Current { get; set; }
    }
}
