using DataSync.Infrastructure;

namespace DataSync.ViewModels
{
    /// <summary>
    /// One row of the Sync Table grid. Rows are updated in place so the grid keeps its scroll position.
    /// </summary>
    public sealed class SyncRangeRow : ObservableObject
    {
        private long _baseIdBegin;
        private string _baseIdEnd;
        private string _currentRecord;
        private string _count;
        private string _share;
        private string _percent;
        private string _status;
        private bool _syncEnabled = true;
        private bool _syncToggleEnabled = true;

        public long BaseIdBegin
        {
            get { return _baseIdBegin; }
            set { SetProperty(ref _baseIdBegin, value); }
        }

        public string BaseIdEnd
        {
            get { return _baseIdEnd; }
            set { SetProperty(ref _baseIdEnd, value); }
        }

        public string CurrentRecord
        {
            get { return _currentRecord; }
            set { SetProperty(ref _currentRecord, value); }
        }

        public string Count
        {
            get { return _count; }
            set { SetProperty(ref _count, value); }
        }

        public string Share
        {
            get { return _share; }
            set { SetProperty(ref _share, value); }
        }

        public string Percent
        {
            get { return _percent; }
            set { SetProperty(ref _percent, value); }
        }

        public string Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }

        /// <summary>Range toggle: on = the sync task copies this range, off = it skips it.</summary>
        public bool SyncEnabled
        {
            get { return _syncEnabled; }
            set { SetProperty(ref _syncEnabled, value); }
        }

        /// <summary>False for Real-time and Synced rows, whose toggle is dimmed (disabled).</summary>
        public bool SyncToggleEnabled
        {
            get { return _syncToggleEnabled; }
            set { SetProperty(ref _syncToggleEnabled, value); }
        }
    }
}
