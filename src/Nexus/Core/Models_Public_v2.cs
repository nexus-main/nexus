// MIT License
// Copyright (c) [2024] [nexus-main]

using System.ComponentModel.DataAnnotations;

namespace Nexus.Core.V2;

/// <summary>
/// A request to register a batch data stream session.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="ResourcePaths">The resource paths to stream.</param>
public record BatchStreamRequest(
    [property: Required] DateTime Begin,
    [property: Required] DateTime End,
    [property: Required] string[] ResourcePaths
);

/// <summary>
/// A registered batch data stream session.
/// </summary>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Channels">The channels to open and consume concurrently.</param>
public record BatchStreamResponse(
    [property: Required] Guid SessionId,
    [property: Required] BatchStreamChannel[] Channels
);

/// <summary>
/// A single channel of a registered batch data stream session.
/// </summary>
/// <param name="ChannelId">The channel identifier.</param>
/// <param name="ResourcePath">The resource path streamed by this channel.</param>
public record BatchStreamChannel(
    [property: Required] Guid ChannelId,
    [property: Required] string ResourcePath
);

/// <summary>
/// The state of a batch data stream session.
/// </summary>
public enum BatchStreamSessionState
{
    /// <summary>
    /// The session is active and streaming data.
    /// </summary>
    Active,

    /// <summary>
    /// The session completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The session failed.
    /// </summary>
    Faulted
}

/// <summary>
/// The status of a batch data stream session.
/// </summary>
/// <param name="State">The session state.</param>
/// <param name="FaultedChannelId">The channel identifier that caused the fault, or null when the fault is not channel-specific.</param>
/// <param name="FaultedChannelResourcePath">The resource path of the channel that caused the fault, or null when the fault is not channel-specific.</param>
/// <param name="FaultReason">A description of why the session failed, or null when the session has not faulted.</param>
public record BatchStreamSessionStatus(
    [property: Required] BatchStreamSessionState State,
    Guid? FaultedChannelId,
    string? FaultedChannelResourcePath,
    string? FaultReason
);
