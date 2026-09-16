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
        private string _percent;
        private string _status;

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
    }
}
