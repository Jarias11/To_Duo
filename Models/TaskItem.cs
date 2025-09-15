using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskMate.Models.Enums;

namespace TaskMate.Models {
    public class TaskItem : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public DateTime? DueDate { get; set; }

        private bool _isCompleted;
        public bool IsCompleted {
            get => _isCompleted;
            set {
                if (_isCompleted == value) return;
                _isCompleted = value;
                OnPropertyChanged();
            }
        }

        public string CreatedBy { get; set; } = string.Empty;   // UserId of sender
        public bool Accepted { get; set; } = true;              // Default true for self-created tasks

        private Assignee _assignedTo = Assignee.Me;
        public Assignee AssignedTo {
            get => _assignedTo;
            set {
                if (_assignedTo == value) return;
                _assignedTo = value;
                OnPropertyChanged();
            }
        }

        public bool IsSuggestion { get; set; } = false;
        public string? MediaPath { get; set; }                  // path to image/video
        public bool IsRecurring { get; set; } = false;
        public DateTime? UpdatedAt { get; set; }                // for conflict/merge
        public bool Deleted { get; set; }                       // soft delete safety

        // Canonical assignee for personal/request flows
        public string AssignedToUserId { get; set; } = string.Empty;

        /// <summary>
        /// UI-friendly label based on user IDs (kept for backward UI bindings).
        /// If IDs don't match, falls back to the enum value name.
        /// </summary>
        public string DisplayAssignee(string myId, string partnerId) =>
            AssignedToUserId == myId ? "Me" :
            AssignedToUserId == partnerId ? "Partner" :
            AssignedTo.ToString();
    }
}