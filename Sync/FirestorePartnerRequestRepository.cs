using Google.Cloud.Firestore;
using TaskMate.Models;
using TaskMate.Sync;

namespace TaskMate.Sync {
	public interface IPartnerRequestRepo {
		IDisposable ListenIncoming(string myUserId, Action<IList<PartnerRequest>> onSnapshot);
		IDisposable ListenOutgoing(string myUserId, Action<IList<PartnerRequest>> onSnapshot);
		Task SendAsync(string fromUserId, string toUserId, string fromDisplayName);
		Task AcceptAsync(string myUserId, string requesterUserId);
		Task DeclineAsync(string myUserId, string requesterUserId);
		Task CancelAsync(string myUserId, string targetUserId);
	}

	public sealed class FirestorePartnerRequestRepository : IPartnerRequestRepo {
		private static CollectionReference IncomingCol(FirestoreDb db, string uid) =>
			db.Collection("users").Document(uid).Collection("partnerRequests_in");
		private static CollectionReference OutgoingCol(FirestoreDb db, string uid) =>
			db.Collection("users").Document(uid).Collection("partnerRequests_out");

		public IDisposable ListenIncoming(string myUserId, Action<IList<PartnerRequest>> onSnapshot) {
			var db = FirestoreClient.GetDb();
			var inner = IncomingCol(db, myUserId).Listen(snap => {
				var list = snap.Documents.Select(MapFrom).ToList();
				onSnapshot(list);
			});
			return new FirestoreListenerHandle(inner);
		}

		public IDisposable ListenOutgoing(string myUserId, Action<IList<PartnerRequest>> onSnapshot) {
			var db = FirestoreClient.GetDb();
			var inner = OutgoingCol(db, myUserId).Listen(snap => {
				var list = snap.Documents.Select(MapFrom).ToList();
				onSnapshot(list);
			});
			return new FirestoreListenerHandle(inner);
		}

		public async Task SendAsync(string fromUserId, string toUserId, string fromDisplayName) {
			var db = FirestoreClient.GetDb();

			// Incoming doc on recipient (doc id = requester uid for easy addressing)
			var inRef = IncomingCol(db, toUserId).Document(fromUserId);
			// Outgoing doc on sender (doc id = recipient uid)
			var outRef = OutgoingCol(db, fromUserId).Document(toUserId);

			var now = Timestamp.GetCurrentTimestamp();
			var incoming = new Dictionary<string, object?> {
				["Id"] = fromUserId,
				["FromUserId"] = fromUserId,
				["FromDisplayName"] = fromDisplayName,
				["ToUserId"] = toUserId,
				["Status"] = "pending",
				["CreatedAt"] = now,
				["UpdatedAt"] = now
			};

			var outgoing = new Dictionary<string, object?> {
				["Id"] = toUserId,
				["FromUserId"] = fromUserId,
				["ToUserId"] = toUserId,
				["Status"] = "pending",
				["CreatedAt"] = now,
				["UpdatedAt"] = now
			};

			var batch = db.StartBatch();
			batch.Set(inRef, incoming, SetOptions.MergeAll);
			batch.Set(outRef, outgoing, SetOptions.MergeAll);
			await batch.CommitAsync();
		}

		public async Task AcceptAsync(string myUserId, string requesterUserId) {
			var db = FirestoreClient.GetDb();
			var inRef = IncomingCol(db, myUserId).Document(requesterUserId);
			var outRef = OutgoingCol(db, requesterUserId).Document(myUserId);
			var now = Timestamp.GetCurrentTimestamp();

			var batch = db.StartBatch();
			batch.Update(inRef, new Dictionary<string, object?> { ["Status"] = "accepted", ["UpdatedAt"] = now });
			batch.Update(outRef, new Dictionary<string, object?> { ["Status"] = "accepted", ["UpdatedAt"] = now });
			await batch.CommitAsync();
		}

		public async Task DeclineAsync(string myUserId, string requesterUserId) {
			var db = FirestoreClient.GetDb();
			var inRef = IncomingCol(db, myUserId).Document(requesterUserId);
			var outRef = OutgoingCol(db, requesterUserId).Document(myUserId);
			var now = Timestamp.GetCurrentTimestamp();

			var batch = db.StartBatch();
			batch.Update(inRef, new Dictionary<string, object?> { ["Status"] = "declined", ["UpdatedAt"] = now });
			batch.Update(outRef, new Dictionary<string, object?> { ["Status"] = "declined", ["UpdatedAt"] = now });
			await batch.CommitAsync();
		}

		public async Task CancelAsync(string myUserId, string targetUserId) {
			var db = FirestoreClient.GetDb();
			var inRef = IncomingCol(db, targetUserId).Document(myUserId);
			var outRef = OutgoingCol(db, myUserId).Document(targetUserId);
			await Task.WhenAll(inRef.DeleteAsync(), outRef.DeleteAsync());
		}

		private static PartnerRequest MapFrom(DocumentSnapshot d) {
			var dict = d.ToDictionary();
			string S(string key) => dict.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
			DateTime? T(string key) => dict.TryGetValue(key, out var v) && v is Timestamp ts ? ts.ToDateTime() : null;

			return new PartnerRequest {
				Id = S("Id"),
				FromUserId = S("FromUserId"),
				FromDisplayName = S("FromDisplayName"),
				ToUserId = S("ToUserId"),
				Status = S("Status"),
				CreatedAt = T("CreatedAt") ?? DateTime.MinValue,
				UpdatedAt = T("UpdatedAt")
			};
		}
	}
}