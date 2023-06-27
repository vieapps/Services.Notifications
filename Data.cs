using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;

namespace net.vieapps.Services.Notifications
{
	[BsonIgnoreExtraElements, Entity(CollectionName = "Notifications", TableName = "T_Notifications", CacheClass = typeof(ServiceComponent), CacheName = "Cache")]
	public class Notification : Repository<Notification>
	{
		public Notification() : base()
			=> this.ID = UtilityService.NewUUID;

		[Sortable(IndexName = "Management")]
		public DateTime Time { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Management")]
		public bool Read { get; set; } = false;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		public Components.Security.Action Action { get; set; } = Components.Security.Action.Update;

		[Property(MaxLength = 32, NotNull = true), Sortable(IndexName = "IDs")]
		public string SenderID { get; set; }

		[Property(MaxLength = 250, NotNull = true)]
		public string SenderName { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "IDs")]
		public string RecipientID { get; set; }

		[Property(MaxLength = 50, NotNull = true), Sortable(IndexName = "Services")]
		public new string ServiceName { get; set; }

		[Property(MaxLength = 50, NotNull = true), Sortable(IndexName = "Services")]
		public new string ObjectName { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "IDs")]
		public override string SystemID { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "IDs")]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "IDs")]
		public override string RepositoryEntityID { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "IDs")]
		public new string ObjectID { get; set; }

		[Property(MaxLength = 250)]
		public override string Title { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		public ApprovalStatus PreviousStatus { get; set; } = ApprovalStatus.Pending;

		[AsJson]
		public string Additionals { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override Privileges OriginalPrivileges { get; set; }
	}
}