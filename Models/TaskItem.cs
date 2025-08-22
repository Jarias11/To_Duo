using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskMate.Models {
    public class TaskItem : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public DateTime? DueDate { get; set; }
        private bool isCompleted = false;
        public bool IsCompleted {
            get => isCompleted;
            set {
                if(isCompleted != value) {
                    isCompleted = value;
                    OnPropertyChanged();
                }
            }
        }
        public string CreatedBy { get; set; } = string.Empty; // UserId of sender
        public bool Accepted { get; set; } = true; // Default true for self-created tasks
        private string? assignedTo = string.Empty;
        public string? AssignedTo {
            get => assignedTo;
            set {
                if(assignedTo != value) {
                    assignedTo = value;
                    OnPropertyChanged(nameof(AssignedTo)); // <- This must exist
                }
            }
        }  // e.g., "Jeremy" or "Brooke"
        public bool IsSuggestion { get; set; } = false;
        public string? MediaPath { get; set; }   // path to image/video
        public bool IsRecurring { get; set; } = false;
        public DateTime? UpdatedAt { get; set; }    // for conflict/merge
        public bool Deleted { get; set; }           // soft delete safety

        // canonical assignee for cloud “requests” later
        public string AssignedToUserId { get; set; }

        public string DisplayAssignee(string myId, string partnerId) =>
    AssignedToUserId == myId ? "Me" :
    AssignedToUserId == partnerId ? "Partner" :
    AssignedTo ?? "Me"; // fallback to legacy
    }
}