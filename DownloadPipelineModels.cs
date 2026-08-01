using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace get_link_manga
{
    internal enum BrowserSessionEngine
    {
        Http = 0,
        WebView2 = 1,
        ChromeFallback = 2
    }

    internal enum DownloadPageState
    {
        Pending = 0,
        Resolved = 1,
        Downloading = 2,
        Downloaded = 3,
        Verified = 4,
        Failed = 5
    }

    [DataContract]
    internal sealed class RetryPolicyProfile
    {
        [DataMember(Order = 1)]
        public int MaxAttempts { get; set; }

        [DataMember(Order = 2)]
        public int BaseDelayMs { get; set; }

        [DataMember(Order = 3)]
        public int MaxDelayMs { get; set; }

        [DataMember(Order = 4)]
        public bool BrowserChallengeNeedsSessionRefresh { get; set; }
    }

    [DataContract]
    internal sealed class SiteDownloadProfile
    {
        [DataMember(Order = 1)]
        public string Id { get; set; }

        [DataMember(Order = 2)]
        public string[] HostAliases { get; set; }

        [DataMember(Order = 3)]
        public bool BrowserSessionPreferred { get; set; }

        [DataMember(Order = 4)]
        public bool ChromeFallbackPreferred { get; set; }

        [DataMember(Order = 5)]
        public int DefaultConcurrencyCap { get; set; }

        [DataMember(Order = 6)]
        public int InterRequestDelayMs { get; set; }

        [DataMember(Order = 7)]
        public string[] AllowedExtensions { get; set; }

        [DataMember(Order = 8)]
        public string[] ChallengeMarkers { get; set; }

        [DataMember(Order = 9)]
        public RetryPolicyProfile RetryPolicy { get; set; }
    }

    [DataContract]
    internal sealed class BrowserSessionCookie
    {
        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string Value { get; set; }

        [DataMember(Order = 3)]
        public string Domain { get; set; }

        [DataMember(Order = 4)]
        public string Path { get; set; }

        [DataMember(Order = 5)]
        public DateTime? ExpiresUtc { get; set; }

        [DataMember(Order = 6)]
        public bool Secure { get; set; }

        [DataMember(Order = 7)]
        public bool HttpOnly { get; set; }
    }

    [DataContract]
    internal sealed class BrowserSessionSnapshot
    {
        [DataMember(Order = 1)]
        public string SourceHost { get; set; }

        [DataMember(Order = 2)]
        public string ResolvedUrl { get; set; }

        [DataMember(Order = 3)]
        public string UserAgent { get; set; }

        [DataMember(Order = 4)]
        public BrowserSessionEngine Engine { get; set; }

        [DataMember(Order = 5)]
        public DateTime AcquiredUtc { get; set; }

        [DataMember(Order = 6)]
        public List<BrowserSessionCookie> Cookies { get; set; } = new List<BrowserSessionCookie>();

        public bool IsExpired(TimeSpan ttl)
        {
            return AcquiredUtc <= DateTime.MinValue || DateTime.UtcNow - AcquiredUtc > ttl;
        }
    }

    [DataContract]
    internal sealed class PageDownloadRecord
    {
        [DataMember(Order = 1)]
        public int PageNumber { get; set; }

        [DataMember(Order = 2)]
        public DownloadPageState State { get; set; }

        [DataMember(Order = 3)]
        public string ReaderUrl { get; set; }

        [DataMember(Order = 4)]
        public string ImageUrl { get; set; }

        [DataMember(Order = 5)]
        public string Referer { get; set; }

        [DataMember(Order = 6)]
        public string ExpectedExtension { get; set; }

        [DataMember(Order = 7)]
        public string ActualExtension { get; set; }

        [DataMember(Order = 8)]
        public long FileSize { get; set; }

        [DataMember(Order = 9)]
        public int AttemptCount { get; set; }

        [DataMember(Order = 10)]
        public string LastError { get; set; }

        [DataMember(Order = 11)]
        public bool Verified { get; set; }

        [DataMember(Order = 12)]
        public string SavedRelativePath { get; set; }

        [DataMember(Order = 13)]
        public DateTime UpdatedUtc { get; set; }
    }

    [DataContract]
    internal sealed class DownloadManifest
    {
        [DataMember(Order = 1)]
        public string Version { get; set; } = "1";

        [DataMember(Order = 2)]
        public string GalleryName { get; set; }

        [DataMember(Order = 3)]
        public string GalleryUrl { get; set; }

        [DataMember(Order = 4)]
        public string SiteProfileId { get; set; }

        [DataMember(Order = 5)]
        public int ExpectedPageCount { get; set; }

        [DataMember(Order = 6)]
        public DateTime UpdatedUtc { get; set; }

        [DataMember(Order = 7)]
        public List<PageDownloadRecord> Pages { get; set; } = new List<PageDownloadRecord>();
    }

    internal sealed class DownloadFileResult
    {
        public string FinalPath { get; set; }
        public string ActualExtension { get; set; }
        public long FileSize { get; set; }
        public string MediaType { get; set; }
    }
}
