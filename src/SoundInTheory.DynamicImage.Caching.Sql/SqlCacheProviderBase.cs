using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Web;
using SoundInTheory.DynamicImage.Util;

namespace SoundInTheory.DynamicImage.Caching.Sql
{
	public abstract class SqlCacheProviderBase : DiskCacheProviderBase
	{
		#region Fields

		private const int CurrentVersion = 1;
		private string _dependentSite;

		#endregion

		#region Properties

		protected abstract string ConnectionString { get; }

		#endregion

		public override void Initialize(string name, NameValueCollection config)
		{
			ProviderUtility.InitialiseConfiguration(config, ref name, "SqlCacheProvider", "SQL Dynamic Image Cache Provider");

			base.Initialize(name, config);

			// Create database tables if not already created.
			CheckDatabaseVersion();

			_dependentSite = config["dependentSite"];
		}

		protected void UseConnection(Action<DbConnection> callback)
		{
			DbProviderFactory dbProviderFactory = GetDbProviderFactory();
			DbConnection conn = dbProviderFactory.CreateConnection();
			conn.ConnectionString = ConnectionString;
			try
			{
				conn.Open();
				callback(conn);
			}
			finally
			{
				conn.Close();			
			}
		}

		protected abstract DbProviderFactory GetDbProviderFactory();

		public override bool ExistsInCache(string cacheKey)
		{
			bool result = false;

			UseConnection(conn =>
			{
				using (DbCommand comm = conn.CreateCommand())
				{
					comm.CommandText = "SELECT * FROM Cache WHERE UniqueKey LIKE @UniqueKey";
					AddCommandParameter(comm, "UniqueKey", DbType.String, cacheKey);
					result = (comm.ExecuteScalar() != null);
				}
			});

			return result;
		}

		public override void AddToCache(string cacheKey, CompositionImage compositionImage, Dependency[] dependencies)
		{
			// Save image to disk.
			SaveImageToDiskCache(compositionImage);

			UseConnection(conn =>
			{
				using (DbCommand comm = conn.CreateCommand())
				{
					comm.CommandText =
						"INSERT INTO Cache (ID, UniqueKey, IsImagePresent, Width, Height, Format, ColorDepth, JpegCompressionLevel) VALUES (@ID, @UniqueKey, @IsImagePresent, @Width, @Height, @Format, @ColorDepth, @JpegCompressionLevel)";
					AddCommandParameter(comm, "ID", DbType.String, compositionImage.Properties.CacheProviderKey);
					AddCommandParameter(comm, "UniqueKey", DbType.String, cacheKey);
					AddCommandParameter(comm, "IsImagePresent", DbType.Boolean,
						compositionImage.Properties.IsImagePresent);
					AddCommandParameter(comm, "Width", DbType.Int32, compositionImage.Properties.Width);
					AddCommandParameter(comm, "Height", DbType.Int32, compositionImage.Properties.Height);
					AddCommandParameter(comm, "Format", DbType.String, compositionImage.Properties.Format);
					AddCommandParameter(comm, "ColorDepth", DbType.Int32, compositionImage.Properties.ColourDepth);
					AddCommandParameter(comm, "JpegCompressionLevel", DbType.Int32,
						compositionImage.Properties.JpegCompressionLevel);
					comm.ExecuteNonQuery();

					foreach (Dependency dependency in dependencies)
					{
						comm.CommandText =
							"INSERT INTO CacheDependencies (CacheID, Text1, Text2, Text3, Text4) VALUES (@CacheID, @Text1, @Text2, @Text3, @Text4);";
						comm.Parameters.Clear();
						AddCommandParameter(comm, "CacheID", DbType.String, compositionImage.Properties.CacheProviderKey);
						AddCommandParameter(comm, "Text1", DbType.String, dependency.Text1);
						AddCommandParameter(comm, "Text2", DbType.String, dependency.Text2);
						AddCommandParameter(comm, "Text3", DbType.String, dependency.Text3);
						AddCommandParameter(comm, "Text4", DbType.String, dependency.Text4);
						comm.ExecuteNonQuery();
					}
				}
			});
		}

		public override ImageProperties GetPropertiesFromCache(string cacheKey)
		{
			ImageProperties imageProperties = null;

			// Create a new database connection.
			UseConnection(conn =>
			{
				using (DbCommand comm = conn.CreateCommand())
				{
					comm.CommandText = "SELECT * FROM Cache WHERE UniqueKey LIKE @UniqueKey";
					AddCommandParameter(comm, "UniqueKey", DbType.String, cacheKey);
					DbDataReader reader = comm.ExecuteReader();
					if (reader.Read())
						imageProperties = GetImageProperties(reader);
				}
			});

			return imageProperties;
		}

		public override void RemoveAllFromCache()
		{
			UseConnection(conn =>
			{
				using (DbCommand comm = conn.CreateCommand())
				{
					comm.CommandText = "SELECT * FROM Cache";
					DbDataReader reader = comm.ExecuteReader();
					while (reader.Read())
					{
						// Delete image from disk cache (and dependent disk caches).
						ImageProperties imageProperties = GetImageProperties(reader);
						DeleteImageFromDiskCache(imageProperties, HttpContext.Current);
						if (!string.IsNullOrEmpty(_dependentSite))
							DeleteImageFromDiskCache(imageProperties, Path.Combine(_dependentSite, "App_Data\\DynamicImage") + imageProperties.CacheProviderKey + "." + imageProperties.FileExtension);
					}
					reader.Close();

					comm.CommandText = "DELETE FROM Cache";
					comm.ExecuteNonQuery();
				}
			});
		}

		public override void RemoveFromCache(Dependency dependency)
		{
			UseConnection(conn =>
			{
				using (DbCommand comm = conn.CreateCommand())
				{
					comm.CommandText = string.Format("SELECT * FROM Cache WHERE EXISTS(SELECT * FROM CacheDependencies WHERE CacheID = Cache.ID AND ({0} AND {1} AND {2} AND {3}))",
						GetFilterText("Text1", dependency.Text1), GetFilterText("Text2", dependency.Text2),
						GetFilterText("Text3", dependency.Text3), GetFilterText("Text4", dependency.Text4));
					AddCommandParameter(comm, "Text1", DbType.String, dependency.Text1);
					AddCommandParameter(comm, "Text2", DbType.String, dependency.Text2);
					AddCommandParameter(comm, "Text3", DbType.String, dependency.Text3);
					AddCommandParameter(comm, "Text4", DbType.String, dependency.Text4);
					DbDataReader reader = comm.ExecuteReader();
					while (reader.Read())
					{
						// Delete image from disk cache (and dependent disk caches).
						ImageProperties imageProperties = GetImageProperties(reader);
						DeleteImageFromDiskCache(imageProperties, HttpContext.Current);
						if (!string.IsNullOrEmpty(_dependentSite))
							DeleteImageFromDiskCache(imageProperties, Path.Combine(_dependentSite, "App_Data\\DynamicImage") + imageProperties.CacheProviderKey + "." + imageProperties.FileExtension);
					}
					reader.Close();

					comm.CommandText = string.Format("DELETE FROM Cache WHERE EXISTS(SELECT * FROM CacheDependencies WHERE CacheID = Cache.ID AND ({0} AND {1} AND {2} AND {3}))",
						GetFilterText("Text1", dependency.Text1), GetFilterText("Text2", dependency.Text2),
						GetFilterText("Text3", dependency.Text3), GetFilterText("Text4", dependency.Text4));
					comm.ExecuteNonQuery();
				}
			});
		}

		private static string GetFilterText(string name, string value)
		{
			if (value == null)
				return name + " IS NULL";
			return name + " = @" + name;
		}

		#region Helper methods

		/// <summary>
		/// Checks the current version of the Sqlite database, and upgrades it
		/// if necessary. We don't use a proper migration tool, because it would bloat
		/// the DynamicImage deployment size.
		/// </summary>
		/// <see cref="http://www.csharphacker.com/technicalblog/index.php/2009/06/28/sqlite-for-c-%E2%80%93-part-3-%E2%80%93-my-first-c-app-using-sqlite-aka-hello-world/"/>
		private void CheckDatabaseVersion()
		{
			try
			{
				UseConnection(conn =>
				{
					using (DbTransaction trans = conn.BeginTransaction())
					{
						using (DbCommand comm = conn.CreateCommand())
						{
							// Add a row to the Version table, if no row exists.
							comm.CommandText = "SELECT VersionNumber FROM Version;";
							object versionNumberObject = comm.ExecuteScalar();
							if (versionNumberObject == null)
							{
								comm.CommandText = "INSERT INTO Version VALUES (0);";
								comm.ExecuteNonQuery();

								versionNumberObject = 0;
							}

							// Check the version number.
							int versionNumber = (int) versionNumberObject;
							if (versionNumber < CurrentVersion)
							{
								if (versionNumber < 1)
								{
									comm.CommandText =
										"CREATE TABLE Cache (ID NVARCHAR(200) NOT NULL PRIMARY KEY, UniqueKey NTEXT NOT NULL, IsImagePresent BIT NOT NULL, Width INT NULL, Height INT NULL, Format NVARCHAR(100) NOT NULL, ColorDepth INT NOT NULL, JpegCompressionLevel INT NULL);";
									comm.ExecuteNonQuery();

									comm.CommandText =
										"CREATE TABLE CacheDependencies (CacheID NVARCHAR(200) NOT NULL CONSTRAINT FK_CacheID REFERENCES Cache(ID) ON DELETE CASCADE, Text1 NVARCHAR(300) NULL, Text2 NVARCHAR(300) NULL, Text3 NVARCHAR(300) NULL, Text4 NVARCHAR(300) NULL);";
									comm.ExecuteNonQuery();
								}
							}

							comm.CommandText = "UPDATE Version SET VersionNumber = " + CurrentVersion;
							comm.ExecuteNonQuery();
						}

						trans.Commit();
					}
				});
			}
			catch (Exception ex)
			{
				throw new ConfigurationErrorsException("Could not update cache database to latest database version.", ex);
			}
		}

		private static ImageProperties GetImageProperties(DbDataReader reader)
		{
			return new ImageProperties
			{
				UniqueKey = (string) reader["UniqueKey"],
				IsImagePresent = (bool) reader["IsImagePresent"],
				Format = (DynamicImageFormat) Enum.Parse(typeof(DynamicImageFormat), (string) reader["Format"]),
				Width = reader["Width"] != DBNull.Value ? (int?) reader["Width"] : null,
				Height = reader["Height"] != DBNull.Value ? (int?) reader["Height"] : null,
				ColourDepth = (int) reader["ColorDepth"],
				JpegCompressionLevel = reader["JpegCompressionLevel"] != DBNull.Value ? (int?) reader["JpegCompressionLevel"] : null,
				CacheProviderKey = (string) reader["ID"]
			};
		}

		private static void AddCommandParameter(DbCommand comm, string name, DbType type, object value)
		{
			DbParameter param = comm.CreateParameter();
			param.ParameterName = name;
			param.DbType = type;
			param.Value = value;
			comm.Parameters.Add(param);
		}

		#endregion
	}
}