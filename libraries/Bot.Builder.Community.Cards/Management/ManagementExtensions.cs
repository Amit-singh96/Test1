﻿using System;
using System.Collections.Generic;
using System.Linq;
using Bot.Builder.Community.Cards.Management.Tree;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bot.Builder.Community.Cards.Management
{
    public static class ManagementExtensions
    {
        public static void SeparateAttachments(this List<Activity> activities)
        {
            BotAssert.ActivityListNotNull(activities);

            // We need to iterate backwards because we're potentially changing the length of the list
            for (int i = activities.Count() - 1; i > -1; i--)
            {
                var activity = activities[i];
                var attachments = activity.Attachments;
                var attachmentCount = attachments?.Count();
                var hasText = activity.Text != null;

                // Should the activity be split into multiple activities?
                if ((attachmentCount > 0 && hasText)
                    || (attachmentCount > 1 && activity.AttachmentLayout != AttachmentLayoutTypes.Carousel))
                {
                    var separateActivities = new List<Activity>();

                    // Prepare the activity for serialization by removing its attachments
                    activity.Attachments = null;

                    // If an activity ID is present then it should be removed
                    // because two activities shouldn't have the same ID
                    activity.Id = null;

                    var jss = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        TypeNameHandling = TypeNameHandling.All,
                    };

                    // Serialize the activity to JSON so it can be cloned
                    var json = JsonConvert.SerializeObject(activity, jss);

                    // Leave the original activity intact just in case it's needed later
                    activity.Attachments = attachments;

                    if (hasText)
                    {
                        var textActivity = JsonConvert.DeserializeObject<Activity>(json, jss);

                        separateActivities.Add(textActivity);
                    }

                    // AttachmentLayoutTypes.List is the default in the sense that
                    // ABS interprets any string other that "carousel" as "list"
                    if (activity.AttachmentLayout == AttachmentLayoutTypes.Carousel)
                    {
                        var carouselActivity = JsonConvert.DeserializeObject<Activity>(json, jss);

                        carouselActivity.Text = null;
                        carouselActivity.Attachments = attachments;
                        separateActivities.Add(carouselActivity);
                    }
                    else
                    {
                        foreach (var attachment in attachments)
                        {
                            var attachmentActivity = JsonConvert.DeserializeObject<Activity>(json, jss);

                            attachmentActivity.Text = null;
                            attachmentActivity.Attachments = new List<Attachment> { attachment };
                            separateActivities.Add(attachmentActivity);
                        }
                    }

                    activities.RemoveAt(i);
                    activities.InsertRange(i, separateActivities);
                }
            }
        }

        /// <summary>
        /// This will convert Adaptive Cards to JObject instances to work around this issue:
        /// https://github.com/microsoft/AdaptiveCards/issues/2148.
        /// </summary>
        /// <param name="activities">A batch of activities.</param>
        public static void ConvertAdaptiveCards(this IEnumerable<IMessageActivity> activities)
        {
            if (activities is null)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            CardTree.Recurse(activities, (Attachment attachment) =>
            {
                if (attachment.ContentType == ContentTypes.AdaptiveCard)
                {
                    attachment.Content = attachment.Content.ToJObject();
                }
            });
        }

        public static void SetLibraryData(this IEnumerable<IMessageActivity> activities, IDictionary<string, object> data, bool merge = false)
        {
            if (activities is null)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            CardTree.SetLibraryData(activities, data, TreeNodeType.Batch, merge);
        }

        // TODO: Expose more methods to apply ID's to more tree nodes
        public static void ApplyIdsToBatch(this IEnumerable<IMessageActivity> activities, DataIdOptions options = null)
        {
            if (activities is null)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            CardTree.ApplyIds(activities, options);
        }

        public static ISet<DataId> GetIdsFromBatch(this IEnumerable<IMessageActivity> activities)
        {
            if (activities is null)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            return CardTree.GetIds(activities);
        }

        public static void AdaptOutgoingCardActions(this IEnumerable<IMessageActivity> activities, string channelId = null)
        {
            if (activities is null)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            foreach (var activity in activities)
            {
                var activityChannelId = channelId ?? activity.ChannelId;

                CardTree.Recurse(activity, (CardAction action) =>
                {
                    var text = action.Text;
                    var value = action.Value;

                    void EnsureText()
                    {
                        if (text == null)
                        {
                            action.Text = value.SerializeIfNeeded();
                        }
                    }

                    void EnsureValue()
                    {
                        if (value == null)
                        {
                            action.Value = text;
                        }
                    }

                    void EnsureStringValue()
                    {
                        if (!(value is string))
                        {
                            if (value == null && text != null)
                            {
                                action.Value = text;
                            }
                            else
                            {
                                action.Value = value.SerializeIfNeeded();
                            }
                        }
                    }

                    void EnsureObjectValue()
                    {
                        // Check if value is null or otherwise primitive
                        if (value.ToJObject() is null)
                        {
                            if (value is string stringValue && stringValue.TryParseJObject() is JObject parsedValue)
                            {
                                action.Value = parsedValue;
                            }
                            else if (text.TryParseJObject() is JObject parsedText)
                            {
                                action.Value = parsedText;
                            }
                        }
                    }

                    if (action.Type == ActionTypes.MessageBack)
                    {
                        switch (activityChannelId)
                        {
                            case Channels.Cortana:
                            case Channels.Skype:
                                // MessageBack does not work on these channels
                                action.Type = ActionTypes.PostBack;
                                break;

                            case Channels.Directline:
                            case Channels.Emulator:
                            case Channels.Line:
                            case Channels.Webchat:
                                EnsureValue();
                                break;

                            case Channels.Email:
                            case Channels.Slack:
                            case Channels.Telegram:
                                EnsureText();
                                break;

                            case Channels.Facebook:
                                EnsureStringValue();
                                break;

                            case Channels.Msteams:
                                EnsureObjectValue();
                                break;
                        }
                    }

                    // Using if instead of else-if so this block can be executed in addition to the previous one
                    if (action.Type == ActionTypes.PostBack)
                    {
                        switch (activityChannelId)
                        {
                            case Channels.Cortana:
                            case Channels.Facebook:
                            case Channels.Slack:
                            case Channels.Telegram:
                                EnsureStringValue();
                                break;

                            case Channels.Directline:
                            case Channels.Email:
                            case Channels.Emulator:
                            case Channels.Line:
                            case Channels.Skype:
                            case Channels.Webchat:
                                EnsureValue();
                                break;

                            case Channels.Msteams:
                                EnsureObjectValue();
                                break;
                        }
                    }

                    if (action.Type == ActionTypes.ImBack)
                    {
                        switch (activityChannelId)
                        {
                            case Channels.Cortana:
                            case Channels.Directline:
                            case Channels.Emulator:
                            case Channels.Facebook:
                            case Channels.Msteams:
                            case Channels.Skype:
                            case Channels.Slack:
                            case Channels.Telegram:
                            case Channels.Webchat:
                                EnsureStringValue();
                                break;

                            case Channels.Email:
                            case Channels.Line:
                                EnsureValue();
                                break;
                        }
                    }
                });
            }
        }

        /// <summary>
        /// This will return null if the incoming activity is not from a button.
        /// The returned value is guaranteed to not be a string.
        /// </summary>
        /// <param name="turnContext">The turn context.</param>
        /// <returns>A button's action data if valid, null otherwise.</returns>
        public static object GetIncomingActionData(this ITurnContext turnContext)
        {
            BotAssert.ContextNotNull(turnContext);

            var activity = turnContext.Activity;

            if (activity is null || activity.Type != ActivityTypes.Message)
            {
                return null;
            }

            var text = activity.Text;
            var parsedText = text.TryParseJObject();
            var value = activity.Value.ToJObject(true);
            var channelData = activity.ChannelData.ToJObject(true); // Channel data will have been serialized into a string in Kik
            var entities = activity.Entities;
            var incomingData = value;

            // Many channels have button responses that are hard to distinguish from user-entered text.
            // A common theme is that button responses often have a property in channel data that isn't
            // present in a typed user-to-bot message, so this local function helps check for that.
            void CheckForChannelDataProperty(string propName, JObject newResult = null)
            {
                if (channelData?.GetValue(propName) != null)
                {
                    incomingData = newResult ?? parsedText;
                }
            }

            switch (activity.ChannelId)
            {
                case Channels.Cortana:

                    // In Cortana, the only defining characteristic of button responses
                    // is that they won't have an "Intent" entity.
                    // This if statement uses `!= true` because we're interpreting a null entities list
                    // as confirmation of a missing "Intent" entity.
                    if (entities?.Any(entity => entity.Type.EqualsCI(EntityTypes.Intent)) != true)
                    {
                        incomingData = parsedText;
                    }

                    break;

                case Channels.Directline:
                case Channels.Emulator:
                case Channels.Webchat:

                    // In Direct Line / Web Chat, button responses can be recognized by a property of channel data.
                    CheckForChannelDataProperty(ChannelData.PostBack, value);
                    CheckForChannelDataProperty(ChannelData.MessageBack, value);

                    break;

                case Channels.Kik:

                    // In Kik, button responses can be recognized by a property of channel data.
                    // Note that this condition will be true because metadata will not be a C# null,
                    // even though it's a "null" JValue.
                    CheckForChannelDataProperty(ChannelData.Metadata);

                    break;

                case Channels.Line:

                    // In LINE, button responses can be recognized by a property of channel data.
                    CheckForChannelDataProperty(ChannelData.LinePostback);

                    break;

                case Channels.Skype:

                    // In Skype, the only defining characteristic of button responses
                    // is that the channel data text does not match the activity text.
                    // This if statement uses `== false` because if the channel data is null or has no text property
                    // then we're interpreting that to mean that this is not a button response.
                    if (channelData?.GetValue(ChannelData.Text)?.ToString().Equals(text) == false)
                    {
                        incomingData = parsedText;
                    }

                    break;

                case Channels.Slack:

                    // In Slack, button responses can be recognized by a property of channel data.
                    CheckForChannelDataProperty(ChannelData.Payload);

                    break;

                case Channels.Telegram:

                    // In Telegram, button responses can be recognized by a property of channel data.
                    CheckForChannelDataProperty(ChannelData.CallbackQuery);

                    break;
            }

            // Teams and Facebook values don't need to be adapted any further

            return incomingData;
        }

        internal static void ApplyIdsToActionData(this JObject actionData, DataIdOptions options)
        {
            var ids = options.GetIds();

            if (ids.Any())
            {
                if (!(actionData[PropertyNames.LibraryData] is JObject libraryData))
                {
                    actionData[PropertyNames.LibraryData] = libraryData = new JObject();
                }

                foreach (var kvp in ids)
                {
                    var scope = kvp.Key;

                    if (options.Overwrite || !libraryData.ContainsKey(scope))
                    {
                        var id = kvp.Value;

                        if (id is null)
                        {
                            if (scope == DataIdScopes.Action)
                            {
                                // Only generate an ID for the action
                                id = DataId.GenerateValue(DataIdScopes.Action);
                            }
                            else
                            {
                                // If any other ID's are null,
                                // don't apply them to the data
                                continue;
                            }
                        }

                        libraryData[scope] = id;
                    }
                }
            }
        }

        internal static T GetLibraryValueFromActionData<T>(this JObject actionData, string key)
            => actionData?[PropertyNames.LibraryData] is JObject libraryData
                && libraryData[key]?.ToObject<object>() is T result ? result : default;

        internal static string GetIdFromActionData(this JObject actionData, string scope = DataIdScopes.Action)
            => actionData?.GetLibraryValueFromActionData<string>(scope);

        internal static IEnumerable<DataId> GetIdsFromActionData(this JObject data)
            => DataId.Scopes.Select(scope => new DataId(scope, data.GetIdFromActionData(scope))).Where(id => id.Value != null);
    }
}
