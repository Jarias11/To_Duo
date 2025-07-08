# 🧑‍🤝‍🧑 TaskMate

**TaskMate** is a lightweight Windows desktop app built with WPF (Windows Presentation Foundation) that helps two people (like friends, couples, or roommates) manage shared to-do lists. It supports offline use with automatic JSON-based storage, task categories, assignments, and color-coded priorities.

---

## ✅ Features

- ✍️ Add, complete, and delete tasks
- 👥 Assign tasks to either person (color-coded)
- 📆 Set due dates with a date picker
- 🗂️ Categorize tasks (with optional filters)
- 🔁 Support for recurring tasks (coming soon)
- 🔔 Desktop notifications/reminders (planned)
- 🧪 Offline-first with local JSON storage and planned sync support

---

## 🛠️ Tech Stack

- **Frontend/UI**: WPF (XAML), MVVM pattern
- **Backend**: C#
- **Storage**: Local JSON files
- **Other tools**: Newtonsoft.Json, INotifyPropertyChanged, Custom ValueConverters

---

## 🗃️ Folder Structure
TaskMate/
├── Models/
├── ViewModels/
├── Views/
├── Converters/
├── App.xaml
├── MainWindow.xaml
├── data.json <-- local task storage
└── README.md

---

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- Visual Studio 2022 or later (with .NET Desktop Development workload)

### Run Locally
1. Clone this repo:
   ```bash
   git clone https://github.com/Jarias11/To_duo.git