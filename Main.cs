#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using System.Diagnostics;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Notifications
{
	public class ServiceComponent : ServiceBase
	{
		public static Cache Cache { get; internal set; }

		public override string ServiceName => "Notifications";

		string NotificationsKey => this.GetKey("Notifications", "VIEApps-56BA2999-NGX-A2E4-Services-4B54-Notification-83EB-Key-693C250DC95D");

		public override void Start(string[] args = null, bool initializeRepository = true, Action<IService> next = null)
		{
			Cache = new Cache($"VIEApps-Services-{this.ServiceName}", Components.Utility.Logger.GetLoggerFactory());
			base.Start(args, initializeRepository, next);
		}

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})");
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationTokenSource.Token);
			try
			{
				JToken json = null;
				var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cts.Token).ConfigureAwait(false);
				switch (requestInfo.Verb.ToUpper())
				{
					case "GET":
						var objectIdentity = requestInfo.GetObjectIdentity();
						json = "search".IsEquals(objectIdentity) || "fetch".IsEquals(objectIdentity)
							? await this.SearchNotificationsAsync(requestInfo, "fetch".IsEquals(objectIdentity), cancellationToken).ConfigureAwait(false)
							: await this.UpdateNotificationAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);
						break;

					case "POST":
						json = await this.CreateNotificationAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);
						break;

					default:
						throw new MethodNotAllowedException(requestInfo.Verb);
				}
				stopwatch.Stop();
				this.WriteLogs(requestInfo, $"Success response - Execution times: {stopwatch.GetElapsedTimes()}");
				if (this.IsDebugResultsEnabled)
					this.WriteLogs(requestInfo, $"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}");
				return json;
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex, stopwatch);
			}
		}

		async Task<JToken> SearchNotificationsAsync(RequestInfo requestInfo, bool asFetch, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Notification>() ?? Filters<Notification>.And();
			if (filter.GetChild("RecipientID") is not FilterBy<Notification> filterByRecipientID)
				(filter as FilterBys<Notification>).Add(Filters<Notification>.Equals("RecipientID", requestInfo.Session.User.ID));
			else
				filterByRecipientID.Value = requestInfo.Session.User.ID;
			filter.Prepare(requestInfo);
			var sort = Sorts<Notification>.Descending("Time");
			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// search
			var totalRecords = await Notification.CountAsync(filter, null, cancellationToken).ConfigureAwait(false);
			var notifications = totalRecords > 0
				? await Notification.FindAsync(filter, sort, pageSize, pageNumber, null, cancellationToken).ConfigureAwait(false)
				: [];

			if (asFetch && pageNumber < 2 && totalRecords > pageSize)
			{
				pageNumber++;
				notifications = notifications.Concat(await Notification.FindAsync(filter, sort, pageSize, pageNumber, null, cancellationToken).ConfigureAwait(false)).ToList();
			}

			// response
			if (asFetch)
				notifications.ForEach(notification => new UpdateMessage
				{
					Type = this.ServiceName,
					DeviceID = requestInfo.Session.DeviceID,
					Data = notification.ToJson()
				}.Send());

			return asFetch
				? new JObject()
				: new JObject
				{
					{ "FilterBy", filter.ToClientJson() },
					{ "SortBy", sort?.ToClientJson() },
					{ "Pagination", new Tuple<long, int, int, int>(totalRecords, Extensions.GetTotalPages(totalRecords, pageSize), pageSize, pageNumber).GetPagination() },
					{ "Objects", notifications.Select(notification => notification.ToJson()).ToJArray() }
				};
		}

		async Task<JToken> CreateNotificationAsync(RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// check permission
			var gotRights = isSystemAdministrator || (requestInfo.Extra != null && requestInfo.Extra.TryGetValue("x-notifications-key", out var notificationsKey) && this.NotificationsKey.IsEquals(notificationsKey));
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetBodyExpando();
			var notification = Notification.CreateInstance(request, "Privileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());
			var response = notification.ToJson();
			if (string.IsNullOrWhiteSpace(notification.RecipientID))
			{
				var recipientIDs = request.Get<List<string>>("Recipients");
				if (recipientIDs == null || recipientIDs.Count < 1)
					throw new InvalidRequestException("No recipient");
				await recipientIDs.ForEachAsync(async userID =>
				{
					notification.ID = UtilityService.NewUUID;
					notification.RecipientID = userID;
					await Notification.CreateAsync(notification, cancellationToken).ConfigureAwait(false);
					response = notification.ToJson();
					(await requestInfo.GetUserSessionsAsync(notification.RecipientID, cancellationToken).ConfigureAwait(false)).Where(info => info.Item4).ForEach(info => new UpdateMessage
					{
						Type = this.ServiceName,
						DeviceID = info.Item2,
						Data = response
					}.Send());
				}, true, false).ConfigureAwait(false);
			}
			else
			{
				await Notification.CreateAsync(notification, cancellationToken).ConfigureAwait(false);
				(await requestInfo.GetUserSessionsAsync(notification.RecipientID, cancellationToken).ConfigureAwait(false)).Where(info => info.Item4).ForEach(info => new UpdateMessage
				{
					Type = this.ServiceName,
					DeviceID = info.Item2,
					Data = response
				}.Send());
			}
			return response;
		}

		async Task<JToken> UpdateNotificationAsync(RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			var notification = await Notification.GetAsync<Notification>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false) ?? throw new InformationNotFoundException();
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(notification.RecipientID);
			if (!gotRights)
				throw new AccessDeniedException();
			var response = notification.ToJson();
			if (!notification.Read)
			{
				notification.Read = true;
				response = notification.ToJson();
				await Notification.UpdateAsync(notification, true, cancellationToken).ConfigureAwait(false);
				(await requestInfo.GetUserSessionsAsync(notification.RecipientID, cancellationToken).ConfigureAwait(false)).Where(info => info.Item4).ForEach(info => new UpdateMessage
				{
					Type = this.ServiceName,
					DeviceID = info.Item2,
					Data = response
				}.Send());
			}
			return response;
		}
	}

	[Repository]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}