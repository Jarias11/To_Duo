using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaskMate.Data.Repositories;
using TaskMate.Models;
using TaskMate.Sync;

namespace TaskMate.Services {
	public sealed class RequestService : IRequestService {
		private readonly IPersonalTaskRepo _personalRepo;
		private readonly IRequestRepo _requestRepo;

		public RequestService()
			: this(new FirestorePersonalTaskRepository(), new FirestoreRequestRepository()) { }

		public RequestService(IPersonalTaskRepo personalRepo, IRequestRepo requestRepo) {
			_personalRepo = personalRepo;
			_requestRepo = requestRepo;
		}

		public IDisposable ListenPersonal(string userId, Action<IList<TaskItem>> onChange)
			=> _personalRepo.Listen(userId, onChange);

		public IDisposable ListenRequests(string groupId, Action<IList<TaskItem>> onChange)
			=> _requestRepo.Listen(groupId, onChange);

		public Task UpsertPersonalAsync(TaskItem item, string userId)
			=> _personalRepo.UpsertAsync(item, userId);

		public Task UpsertRequestAsync(TaskItem item, string groupId)
			=> _requestRepo.UpsertAsync(item, groupId);

		public Task AcceptRequestAsync(TaskItem item, string groupId, string myUserId)
			=> _requestRepo.AcceptAsync(item, groupId, myUserId);

		public Task DeleteRequestAsync(Guid requestId, string groupId)
			=> _requestRepo.DeleteAsync(requestId, groupId);

		public Task DeletePersonalAsync(Guid id, string userId)
			=> _personalRepo.DeleteAsync(id, userId);
	}
}