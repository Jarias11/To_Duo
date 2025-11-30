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

		// FirestorePartnerRequestRepository.cs
		// me    = recipient user id  (userId in the rules / owner of partnerRequests_in)
		// other = sender   user id  (otherId in the rules)

		private async Task UpdateStatusBoth(string myUserId, string otherUserId, string status)
{
    var now = DateTime.UtcNow;

    // Determine sender → recipient roles
    string sender, recipient;

    // If I initiated the request
    // then outbound row must exist under my userId
    sender = myUserId;
    recipient = otherUserId;

    // But if the request was FROM the other user TO me
    // then inbound row exists under myUserId
    // so flip roles
    bool iWasRecipient = false;

    // Check which row exists:
    // users/{myUserId}/partnerRequests_in/{otherUserId}
    if (await _rest.DocExistsAsync($"users/{myUserId}/partnerRequests_in/{otherUserId}"))
    {
        sender = otherUserId;
        recipient = myUserId;
        iWasRecipient = true;
    }

    // Write inbound (recipient’s collection)
    var inboundTask = _rest.SetDocAsync(
        $"users/{recipient}/partnerRequests_in/{sender}",
        new {
            FromUserId = F.Str(sender),
            ToUserId   = F.Str(recipient),
            Status     = F.Str(status),
            UpdatedAt  = F.Ts(now)
        }
    );

    // Write outbound (sender’s collection)
    var outboundTask = _rest.SetDocAsync(
        $"users/{sender}/partnerRequests_out/{recipient}",
        new {
            FromUserId = F.Str(sender),
            ToUserId   = F.Str(recipient),
            Status     = F.Str(status),
            UpdatedAt  = F.Ts(now)
        }
    );

    await Task.WhenAll(inboundTask, outboundTask).ConfigureAwait(false);
}


		private IDisposable StartPolling(string collectionPath, Action<IList<PartnerRequest>> onSnapshot) {
	var cts = new CancellationTokenSource();
	_ = Task.Run(async () => {
		var last = new Dictionary<string, DateTimeOffset>();
		DateTimeOffset? lastNewest = null;
		var first = true;

		while(!cts.IsCancellationRequested) {
			try {
				// 1) Cheap head
				var headDocs = await _rest.ListDocsAsync(
					collectionPath: collectionPath,
					orderBy: "UpdatedAt desc",
					pageSize: 1
				).ConfigureAwait(false);

				DateTimeOffset? newest = null;
				if(headDocs.Count > 0) {
					var head = MapFromDoc(headDocs[0]);
					newest = new DateTimeOffset(head.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero);
				}

				if(!first && newest.HasValue && lastNewest.HasValue && newest.Value == lastNewest.Value) {
					goto DelayOnly;
				}

				lastNewest = newest;
				first = false;

				// 2) Full list
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
						r => new DateTimeOffset(r.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero));
					onSnapshot(list);
				}
			}
			catch {
				/* keep polling */
			}

DelayOnly:
			try { await Task.Delay(8000, cts.Token).ConfigureAwait(false); } catch { }
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
