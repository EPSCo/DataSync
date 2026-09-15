using DataSync.Infrastructure;

namespace DataSync.ViewModels
{
    /// <summary>
    /// One row of the Sync Table grid. Rows are updated in place so the grid keeps its scroll position.
    /// </summary>
    public sealed class SyncRangeRow : ObservableObject
    {
        private long _baseIdBegin;
        private long _baseIdEnd;
        private string _startSyncPoint;
        private string _status;

        public long BaseIdBegin
        {
            get { return _baseIdBegin; }
            set { SetProperty(ref _baseIdBegin, value); }
        }

        public long BaseIdEnd
        {
            get { return _baseIdEnd; }
            set { SetProperty(ref _baseIdEnd, value); }
        }

        public string StartSyncPoint
        {
            get { return _startSyncPoint; }
            set { SetProperty(ref _startSyncPoint, value); }
        }

        public string Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }
    }
}
