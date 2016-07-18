// Created by: egr
// Created at: 19.09.2012
// � 2012-2016 Alexander Egorov

using logviewer.engine;
using logviewer.logic.Annotations;
using logviewer.logic.models;

namespace logviewer.logic.ui
{
    public interface ILogView : IUpdatable
    {
        string LogPath { get; set; }
        string LogFileName { get; }
        [PublicAPI]
        string HumanReadableLogSize { get; set; }
        string LogInfo { get; set; }
        void ClearRecentFilesList();
        void CreateRecentFileItem(string file);
        bool OpenLogFile();
        void ReadLog();
        void OnFailureRead(string errorMessage);
        bool OpenExport(string path);
        void SaveRtf();
        [PublicAPI]
        void SetCurrentPage(int page);
        void SetPageSize(int size);
        void Initialize();
        [PublicAPI]
        void FocusOnTextFilterControl();
        void SetProgress(LoadProgress progress);
        void SetLogProgressCustomText(string text);
        void StartLoadingLog(string path);
        void SetLoadedFileCapltion(string path);
        void SetFileEncoding(string encoding);
        void StartReading();
        void AddFilterItems(string[] items);
        void ClearTemplatesList();
        void CreateTemplateSelectionItem(ParsingTemplate template, int current);
    }
}