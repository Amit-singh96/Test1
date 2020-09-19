﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;

namespace Bot.Builder.Community.Cards.Management
{
    public class CardManagerMiddleware : IMiddleware
    {
        public CardManagerMiddleware(CardManager manager)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public CardManager Manager { get; }

        public IList<string> ChannelsWithMessageUpdates { get; } = new List<string> { Channels.Msteams, Channels.Skype, Channels.Slack, Channels.Telegram };

        // TODO: Combine UpdatingOptions and NonUpdatingOptions into one property
        public CardManagerMiddlewareOptions UpdatingOptions { get; set; } = GetDefaultUpdatingOptions();

        public CardManagerMiddlewareOptions NonUpdatingOptions { get; set; } = GetDefaultNonUpdatingOptions();

        public static CardManagerMiddlewareOptions GetDefaultUpdatingOptions() => new CardManagerMiddlewareOptions
        {
            AutoAdaptOutgoingCardActions = true,
            AutoApplyIds = true,
            AutoClearEnabledOnSend = false,
            AutoConvertAdaptiveCards = true,
            AutoDeleteOnAction = true,
            AutoDisableOnAction = false,
            AutoEnableOnSend = false,
            AutoSaveActivitiesOnSend = true,
            AutoSeparateAttachmentsOnSend = true,
            IdTrackingStyle = TrackingStyle.None,
            IdOptions = new DataIdOptions(DataIdScopes.Action),
        };

        public static CardManagerMiddlewareOptions GetDefaultNonUpdatingOptions() => new CardManagerMiddlewareOptions
        {
            AutoAdaptOutgoingCardActions = true,
            AutoApplyIds = true,
            AutoClearEnabledOnSend = true,
            AutoConvertAdaptiveCards = true,
            AutoDeleteOnAction = false,
            AutoDisableOnAction = true,
            AutoEnableOnSend = true,
            AutoSaveActivitiesOnSend = false,
            AutoSeparateAttachmentsOnSend = false,
            IdTrackingStyle = TrackingStyle.TrackEnabled,
            IdOptions = new DataIdOptions(DataIdScopes.Action),
        };

        public CardManagerMiddleware SetAutoAdaptOutgoingCardActions(bool autoAdaptOutgoingCardActions)
        {
            UpdatingOptions.AutoAdaptOutgoingCardActions = autoAdaptOutgoingCardActions;
            NonUpdatingOptions.AutoAdaptOutgoingCardActions = autoAdaptOutgoingCardActions;

            return this;
        }

        public CardManagerMiddleware SetAutoApplyIds(bool autoApplyIds)
        {
            UpdatingOptions.AutoApplyIds = autoApplyIds;
            NonUpdatingOptions.AutoApplyIds = autoApplyIds;

            return this;
        }

        public CardManagerMiddleware SetAutoConvertAdaptiveCards(bool autoConvertAdaptiveCards)
        {
            UpdatingOptions.AutoConvertAdaptiveCards = autoConvertAdaptiveCards;
            NonUpdatingOptions.AutoConvertAdaptiveCards = autoConvertAdaptiveCards;

            return this;
        }

        public CardManagerMiddleware SetIdOptions(DataIdOptions idOptions)
        {
            UpdatingOptions.IdOptions = idOptions;
            NonUpdatingOptions.IdOptions = idOptions;

            return this;
        }

        public async Task OnTurnAsync(ITurnContext turnContext, NextDelegate next, CancellationToken cancellationToken = default)
        {
            BotAssert.ContextNotNull(turnContext);

            if (next is null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            turnContext.TurnState.Add(new CardManagerTurnState());

            var options = GetOptionsForChannel(turnContext.Activity.ChannelId);
            var shouldProceed = true;

            // Is this activity from a button?
            if (turnContext.GetIncomingActionData() is JObject data)
            {
                var incomingIds = data.GetIdsFromActionData();
                var autoDeactivate = data.GetLibraryValueFromActionData<bool?>(Behaviors.AutoDeactivate);

                // Actions should not be disabled if they have no data ID's
                if (incomingIds.Count() > 0)
                {
                    if (options.IdTrackingStyle != TrackingStyle.None && autoDeactivate != false)
                    {
                        // Whether we should proceed by default depends on the ID-tracking style
                        shouldProceed = options.IdTrackingStyle == TrackingStyle.TrackDisabled;

                        var state = await Manager.GetStateAsync(turnContext, cancellationToken).ConfigureAwait(false);

                        foreach (var incomingId in incomingIds)
                        {
                            state.DataIdsByScope.TryGetValue(incomingId.Key, out var trackedSet);

                            var setContainsId = trackedSet?.Contains(incomingId.Value) == true;

                            if (setContainsId)
                            {
                                // Proceed if the presence of the ID indicates that the ID is enabled (opt-in logic),
                                // short-circuit if the presence of the ID indicates that the ID is disabled (opt-out logic)
                                shouldProceed = options.IdTrackingStyle == TrackingStyle.TrackEnabled;
                            }

                            if (options.AutoDisableOnAction || autoDeactivate == true)
                            {
                                // This might disable an already-disabled ID but that's okay
                                await Manager.DisableIdAsync(
                                    turnContext,
                                    incomingId,
                                    options.IdTrackingStyle,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    if ((options.AutoDeleteOnAction && autoDeactivate != false) || (options == UpdatingOptions && autoDeactivate == true))
                    {
                        // If there are multiple ID scopes in use, just delete the one with the largest range
                        var scope = DataId.Scopes.ElementAtOrDefault(incomingIds.Max(id => DataId.Scopes.IndexOf(id.Key)));

                        await Manager.DeleteActionSourceAsync(turnContext, scope, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            turnContext.OnSendActivities(OnSendActivities);
            turnContext.OnUpdateActivity(OnUpdateActivity);
            turnContext.OnDeleteActivity(OnDeleteActivity);

            if (shouldProceed)
            {
                // If this is not called, the middleware chain is effectively "short-circuited"
                await next(cancellationToken).ConfigureAwait(false);
            }
        }

        // This will be called by the Bot Builder SDK and all three of these parameters are guaranteed to not be null
        private async Task<ResourceResponse[]> OnSendActivities(ITurnContext turnContext, List<Activity> activities, Func<Task<ResourceResponse[]>> next)
        {
            var options = GetOptionsForChannel(turnContext.Activity.ChannelId);

            // This method should be executed even if the bot is only sending text
            if (options.AutoClearEnabledOnSend
                && options.IdTrackingStyle == TrackingStyle.TrackEnabled
                && activities.Any(activity => activity.Type == ActivityTypes.Message))
            {
                // TODO: Add an equivalent of this for channels with updates
                await Manager.ClearTrackedIdsAsync(turnContext).ConfigureAwait(false);
            }

            // The other methods will only be effective if the bot is sending attachments
            if (activities.Any(activity => activity.Attachments?.Any() != true))
            {
                return await next().ConfigureAwait(false);
            }

            if (options.AutoConvertAdaptiveCards)
            {
                activities.ConvertAdaptiveCards();
            }

            if (options.AutoSeparateAttachmentsOnSend)
            {
                activities.SeparateAttachments();
            }

            if (options.AutoAdaptOutgoingCardActions)
            {
                activities.AdaptOutgoingCardActions(turnContext.Activity.ChannelId);
            }

            if (options.AutoApplyIds)
            {
                activities.ApplyIdsToBatch(options.IdOptions);
            }

            // The resource response ID's will be automatically applied to the activities
            // so this return value is only passed along as the outer return value
            // and is not used for tracking/management.
            // The needed activity ID's can be extracted from the activities directly.
            var resourceResponses = await next().ConfigureAwait(false);

            if (options.AutoEnableOnSend && options.IdTrackingStyle == TrackingStyle.TrackEnabled)
            {
                foreach (var dataId in activities.GetIdsFromBatch())
                {
                    await Manager.EnableIdAsync(turnContext, dataId, options.IdTrackingStyle).ConfigureAwait(false);
                }
            }

            if (options.AutoSaveActivitiesOnSend)
            {
                await Manager.SaveActivitiesAsync(turnContext, activities).ConfigureAwait(false);
            }

            return resourceResponses;
        }

        // This will be called by the Bot Builder SDK and all three of these parameters are guaranteed to not be null
        private async Task<ResourceResponse> OnUpdateActivity(ITurnContext turnContext, Activity activity, Func<Task<ResourceResponse>> next)
        {
            var ignoreUpdate = turnContext.TurnState.Get<CardManagerTurnState>()?.MiddlewareIgnoreUpdate;

            // Removing the item also checks if it was present
            if (ignoreUpdate.Remove(activity))
            {
                return await next().ConfigureAwait(false);
            }

            var options = GetOptionsForChannel(turnContext.Activity.ChannelId);
            var activities = new[] { activity };

            // Some functionality that is in OnSendActivities is intentionally omitted here.
            // We don't clear enabled ID's because a new activity isn't being sent.
            // We don't separate attachments because it's impossible to update an activity with multiple activities.

            if (options.AutoConvertAdaptiveCards)
            {
                activities.ConvertAdaptiveCards();
            }

            if (options.AutoAdaptOutgoingCardActions)
            {
                activities.AdaptOutgoingCardActions(turnContext.Activity.ChannelId);
            }

            if (options.AutoApplyIds)
            {
                activities.ApplyIdsToBatch(options.IdOptions);
            }

            // The resource response ID will already be the ID of the activity
            // so this return value is only passed along as the outer return value
            // and is not used for tracking/management.
            // The needed activity ID can be extracted from the activity directly.
            var resourceResponse = await next().ConfigureAwait(false);

            if (options.AutoEnableOnSend && options.IdTrackingStyle == TrackingStyle.TrackEnabled)
            {
                foreach (var dataId in activities.GetIdsFromBatch())
                {
                    await Manager.EnableIdAsync(turnContext, dataId, options.IdTrackingStyle).ConfigureAwait(false);
                }
            }

            var state = await Manager.GetStateAsync(turnContext).ConfigureAwait(false);
            var savedActivity = state.SavedActivities.FirstOrDefault(a => a.Id == activity.Id);

            if (options.AutoSaveActivitiesOnSend || savedActivity != null)
            {
                await Manager.SaveActivitiesAsync(turnContext, activities).ConfigureAwait(false);
            }

            return resourceResponse;
        }

        // This will be called by the Bot Builder SDK and all three of these parameters are guaranteed to not be null
        private async Task OnDeleteActivity(ITurnContext turnContext, ConversationReference reference, Func<Task> next)
        {
            await next().ConfigureAwait(false);

            var ignoreDelete = turnContext.TurnState.Get<CardManagerTurnState>()?.MiddlewareIgnoreDelete;

            // Removing the item also checks if it was present
            if (ignoreDelete.Remove(reference.ActivityId))
            {
                return;
            }

            await Manager.UnsaveActivityAsync(turnContext, reference.ActivityId).ConfigureAwait(false); 
        }

        private CardManagerMiddlewareOptions GetOptionsForChannel(string channelId)
        {
            return ChannelsWithMessageUpdates.Contains(channelId)
                ? UpdatingOptions ?? GetDefaultUpdatingOptions()
                : NonUpdatingOptions ?? GetDefaultNonUpdatingOptions();
        }
    }
}
