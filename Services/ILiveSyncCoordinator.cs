// ILiveSyncCoordinator.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using TaskMate.Models;

namespace TaskMate.Services
{
    public interface ILiveSyncCoordinator : IDisposable
    {
        // Wire up the collections + views the coordinator should maintain
        void Attach(
            ObservableCollection<TaskItem> tasks,
            ObservableCollection<TaskItem> pendingRequests,
            ObservableCollection<PartnerRequest> incomingReqs,
            ObservableCollection<PartnerRequest> outgoingReqs,
            ICollectionView myTasksView,
            ICollectionView partnerTasksView);

        // Start all listeners (personal, partner-requests, and if verified, partner + group)
        Task StartAsync();

        // Rebind listeners when PartnerId/GroupId changes
        Task ReloadForPartnerAsync();

        // Raised when the other side disconnects (so the VM can toggle UI flags etc.)
        event Action PartnerDisconnected;
    }
}