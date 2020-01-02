﻿using System.Runtime.Serialization;
using Microsoft.Bot.Schema;

namespace Bot.Builder.Community.Cards
{
    /// <summary>
    /// This enum defines ID types with progressively increasing scope sizes.
    /// Note: The <see cref="EnumMemberAttribute">EnumMember</see> attributes are used
    /// for when this enum is a dictionary key and the dictionary is serialized into JSON.
    /// </summary>
    public enum IdType
    {
        /// <summary>
        /// An action ID should be globally unique and not found on different actions.
        /// </summary>
        [EnumMember(Value = "action")]
        Action,

        /// <summary>
        /// A card ID should be the same for every action on a single card.
        /// </summary>
        [EnumMember(Value = "card")]
        Card,

        /// <summary>
        /// A carousel ID should be the same for every action across all card attachments
        /// in a single activity. This is not called an activity ID to avoid confusion because
        /// that would be ambiguous with an activity's actual activity ID from the channel.
        /// </summary>
        [EnumMember(Value = "carousel")]
        Carousel,

        /// <summary>
        /// A batch ID should be the same for every action in a batch of activities.
        /// </summary>
        [EnumMember(Value = "batch")]
        Batch,
    }
}