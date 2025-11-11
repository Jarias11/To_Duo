// Services/Sync/FirestorePartnerRequestRepository.cs
using System.Text.Json;
using TaskMate.Models;
using TaskMate.Services;

namespace TaskMate.Sync {
	public interface IPartnerRequestRepo {
		IDisposable ListenIncoming(string myUserId, Action<IList<PartnerRequest>> onSnapshot);
		IDisposable ListenOutgoing(string myUserId, Action<IList<PartnerRequest>> onSnapshot);
		Task SendAsync(string fromUserId, string toUserId, string fromDisplayName);
		Task AcceptAsync(string myUserId, string requesterUserId);
		Task DeclineAsync(string myUserId, string requesterUserId);
		Task CancelAsync(string myUserId, string targetUserId);
		Task DisconnectAsync(string myUserId, string partnerUserId);
		Task PurgePairAsync(string userA, string userB);
	}

	/// <summary>
	/// REST-only partner-requests repo.
	/// Collections:
	///   users/{uid}/partnerRequests_in
	///   users/{uid}/partnerRequests_out
	/// Doc shape: { FromUserId, ToUserId, Status, FromDisplayName, UpdatedAt }
	/// </summary>
	public sealed class FirestorePartnerRequestRepository : IPartnerRequestRepo {
		private readonly FirestoreRestClient _rest;

		public FirestorePartnerRequestRepository(FirestoreRestClient rest) {
			_rest = rest ?? throw new ArgumentNullException(nameof(rest));
		}

		public IDisposable ListenIncoming(string myUserId, Action<IList<PartnerRequest>> onSnapshot)
			=> StartPolling($"users/{myUserId}/partnerRequests_in", onSnapshot);

		public IDisposable ListenOutgoing(string myUserId, Action<IList<PartnerRequest>> onSnapshot)
			=> StartPolling($"users/{myUserId}/partnerRequests_out", onSnapshot);

		public async Task SendAsync(string fromUserId, string toUserId, string fromDisplayName) {
			var now = DateTime.UtcNow;

			// To recipient: inbound row
			await _rest.SetDocAsync(
				$"users/{toUserId}/partnerRequests_in/{fromUserId}",
				new {
					FromUserId = F.Str(fromUserId),
					ToUserId = F.Str(toUserId),
					FromDisplayName = F.Str(fromDisplayName ?? ""),
					Status = F.Str("pending"),
					UpdatedAt = F.Ts(now)
				}
			).ConfigureAwait(false);

			// To sender: outbound mirror
			await _rest.SetDocAsync(
				$"users/{fromUserId}/partnerRequests_out/{toUserId}",
				new {
					FromUserId = F.Str(fromUserId),
					ToUserId = F.Str(toUserId),
					FromDisplayName = F.Str(fromDisplayName ?? ""),
					Status = F.Str("pending"),
					UpdatedAt = F.Ts(now)
				}
			).ConfigureAwait(false);
		}

		public Task AcceptAsync(string myUserId, string requesterUserId)
			=> UpdateStatusBoth(myUserId, requesterUserId, "accepted");

		public Task DeclineAsync(string myUserId, string requesterUserId)
			=> UpdateStatusBoth(myUserId, requesterUserId, "declined");

		public Task CancelAsync(string myUserId, string targetUserId)
			=> UpdateStatusBoth(myUserId, targetUserId, "canceled");

		public Task DisconnectAsync(string myUserId, string partnerUserId)
			=> UpdateStatusBoth(myUserId, partnerUserId, "disconnected");

		public async Task PurgePairAsync(string userA, string userB) {
			foreach(var path in new[]
			{
				$"users/{userA}/partnerRequests_in/{userB}",
				$"users/{userA}/partnerRequests_out/{userB}",
				$"users/{userB}/partnerRequests_in/{userA}",
				$"users/{userB}/partnerRequests_out/{userA}",
			}) {
				try { await _rest.DeleteDocAsync(path).ConfigureAwait(false); } catch { }
			}
		}

		// --- Internals --------------------------------------------------------

		private async Task UpdateStatusBoth(string me, string other, string status) {
			var now = DateTime.UtcNow;

			await _rest.SetDocAsync(
		$"users/{me}/partnerRequests_in/{other}",
		new {
			FromUserId = F.Str(other),
			ToUserId = F.Str(me),
			Status = F.Str(status),
			UpdatedAt = F.Ts(now)
		}
	).ConfigureAwait(false);

			// other's OUTBOUND mirror (sender=other, recipient=me)
			await _rest.SetDocAsync(
				$"users/{other}/partnerRequests_out/{me}",
				new {
					FromUserId = F.Str(other),
					ToUserId = F.Str(me),
					Status = F.Str(status),
					UpdatedAt = F.Ts(now)
				}
			).ConfigureAwait(false);
		}

		private IDisposable StartPolling(string collectionPath, Action<IList<PartnerRequest>> onSnapshot) {
			var cts = new CancellationTokenSource();
			_ = Task.Run(async () => {
				var last = new Dictionary<string, DateTimeOffset>();
				while(!cts.IsCancellationRequested) {
					try {
						var list = await QueryCollectionAsync(collectionPath).ConfigureAwait(false);

						bool changed = list.Count != last.Count ||
									   list.Any(r => {
										   var key = r.Id ?? (r.FromUserId + "_" + r.ToUserId);
										   var ts = new DateTimeOffset(r.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero);
										   return !last.TryGetValue(key, out var prev) || prev != ts;
									   });

						if(changed) {
							last = list.ToDictionary(
								r => r.Id ?? (r.FromUserId + "_" + r.ToUserId),
								r => new DateTimeOffset(r.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero)
							);
							onSnapshot(list);
						}
					}
					catch { /* keep polling */ }

					try { await Task.Delay(1500, cts.Token).ConfigureAwait(false); } catch { }
				}
			}, cts.Token);

			return new FirestoreListenerHandle(() => cts.Cancel());
		}

		private async Task<IList<PartnerRequest>> QueryCollectionAsync(string collectionPath) {
			// Use precise path + orderBy, same as Personal repo
			var docs = await _rest.ListDocsAsync(
				collectionPath: collectionPath,
				orderBy: "UpdatedAt desc",
				pageSize: 50
			).ConfigureAwait(false);

			var result = new List<PartnerRequest>(docs.Count);
			foreach(var doc in docs)
				result.Add(MapFromDoc(doc));
			return result;
		}

		private static PartnerRequest MapFromDoc(JsonElement doc) {
			// doc.name = ".../documents/{collection}/{id}"
			string id = doc.GetProperty("name").GetString()!.Split('/').Last();
			var f = doc.GetProperty("fields");

			return new PartnerRequest {
				Id = id,
				FromUserId = ReadString(f, "FromUserId") ?? "",
				ToUserId = ReadString(f, "ToUserId") ?? "",
				FromDisplayName = ReadString(f, "FromDisplayName"),
				Status = ReadString(f, "Status") ?? "pending",
				UpdatedAt = TryReadTs(f, "UpdatedAt")
			};
		}

		private static string? ReadString(JsonElement fields, string name)
			=> fields.TryGetProperty(name, out var v) && v.TryGetProperty("stringValue", out var s) ? s.GetString() : null;

		private static DateTime? TryReadTs(JsonElement fields, string name)
			=> fields.TryGetProperty(name, out var v) && v.TryGetProperty("timestampValue", out var t)
				? DateTime.Parse(t.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind)
				: (DateTime?)null;
	}
}
