﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AdaptiveCards;
using Bot.Builder.Community.Cards.Management;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Adapters;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Bot.Builder.Community.Cards.Tests
{
    [TestClass]
    public class CardManagerMiddlewareTests
    {
        [TestMethod]
        public async Task NonUpdatingSettingsAreUsed()
        {
            await RunTestFlow();
        }

        private static async Task RunTestFlow(CardManagerMiddlewareOptions options = null)
        {
            var middleware = new CardManagerMiddleware(new CardManager(new UserState(new MemoryStorage())));

            if (options != null)
            {
                middleware.NonUpdatingOptions = options;
                middleware.UpdatingOptions = ReverseMiddlewareOptions(options);
            }

            await RunTestFlow(middleware);
        }

        private static async Task RunTestFlow(CardManagerMiddleware middleware, string channel = Channels.Test, bool updatingOptionsExpected = false)
        {
            const string USERSAYS_TEXTPLEASE = "text please";
            const string USERSAYS_ATTACHMENTSPLEASE = "attachment please";
            const string USERSAYS_BOTHPLEASE = "both please";
            const string ACTIONID1 = "action ID 1";
            const string ACTIONID2 = "action ID 2";
            const string ACTIONID3 = "action ID 3";
            const string ACTIONID4 = "action ID 4";
            const string ACTIONID5 = "action ID 5";
            const string ACTIONID6 = "action ID 6";
            const string CARDID = "card ID";
            const string CAROUSELID = "carousel ID";
            const string BATCHID = "batch ID";
            const string CHOICEINPUTID = "choice set input ID";
            const string DATEINPUTID = "date input ID";
            const string NUMBERINPUTID = "number input ID";
            const string TEXTINPUTID = "text input ID";
            const string TIMEINPUTID = "time input ID";
            const string TOGGLEINPUTID = "toggle input ID";
            const string USERENTEREDCHOICE = "User-entered choice";
            const string USERENTEREDDATE = "User-entered date";
            const double USERENTEREDNUMBER1 = 1;
            const double USERENTEREDNUMBER2 = 2;
            const string USERENTEREDTEXT = "User-entered text";
            const string USERENTEREDTIME = "User-entered time";
            const string USERENTEREDTOGGLE = "User-entered toggle";

            CardManagerMiddlewareOptions GetExpectedOptions() => updatingOptionsExpected ? middleware.UpdatingOptions : middleware.NonUpdatingOptions;

            var adapter = new TestAdapter().Use(middleware);

            await new TestFlow(adapter, async (turnContext, cancellationToken) =>
            {
                if (turnContext.GetIncomingActionData().ToJObject() is JObject data)
                {

                }
                else
                {
                    var state = await middleware.Manager.GetStateAsync(turnContext);
                    var options = GetExpectedOptions();
                    var idTypes = options.IdOptions.GetIdTypes();
                    var newActionIds = 0;
                    var newCardIds = 0;
                    var newCarouselIds = 0;
                    var newBatchIds = 0;
                    var actionsSent = 0;
                    var cardsSent = 0;

                    switch (turnContext.Activity.Text)
                    {
                        case USERSAYS_TEXTPLEASE:
                            await turnContext.SendActivityAsync("Here is some text");
                            break;

                        case USERSAYS_ATTACHMENTSPLEASE:

                            await turnContext.SendActivityAsync(MessageFactory.Attachment(
                                new List<Attachment>
                                {
                                    new Attachment
                                    {
                                        ContentType = ContentTypes.AdaptiveCard,
                                        Content = new AdaptiveCard("1.0")
                                        {
                                            Body = new List<AdaptiveElement>
                                            {
                                                new AdaptiveChoiceSetInput
                                                {
                                                    Id = CHOICEINPUTID,
                                                },
                                            },
                                            SelectAction = new AdaptiveSubmitAction(),
                                        },
                                    },
                                    new AnimationCard(buttons: new List<CardAction>
                                    {
                                        new CardAction(ActionTypes.PostBack, value: new object()),
                                        new CardAction(ActionTypes.MessageBack, value: new object()),
                                    }).ToAttachment(),
                                }));

                            actionsSent = 3;
                            cardsSent = 2;

                            break;

                        case USERSAYS_BOTHPLEASE:

                            await turnContext.SendActivityAsync(MessageFactory.Carousel(
                                new List<Attachment>
                                {
                                    new AudioCard(buttons: new List<CardAction>
                                    {
                                        new CardAction(ActionTypes.PostBack, value: new object()),
                                        new CardAction(ActionTypes.MessageBack, value: new object()),
                                    }).ToAttachment(),
                                    new HeroCard(buttons: new List<CardAction>
                                    {
                                        new CardAction(ActionTypes.PostBack, value: new object()),
                                    }).ToAttachment(),
                                    new Attachment
                                    {
                                        ContentType = ContentTypes.AdaptiveCard,
                                        Content = new AdaptiveCard("1.1")
                                        {
                                            Actions = new List<AdaptiveAction>
                                            {
                                                new AdaptiveSubmitAction(),
                                            },
                                        },
                                    },
                                },
                                "And text too"));

                            actionsSent = 4;
                            cardsSent = 3;

                            break;
                    }

                    if (options.AutoApplyIds
                        && options.AutoEnableOnSend
                        && options.IdTrackingStyle == TrackingStyle.TrackEnabled)
                    {
                        newActionIds = idTypes.Contains(DataIdTypes.Action) ? actionsSent : 0;
                        newCardIds = idTypes.Contains(DataIdTypes.Card) ? cardsSent : 0;
                        newCarouselIds = idTypes.Contains(DataIdTypes.Carousel) && actionsSent > 0 ? 1 : 0;
                        newBatchIds = idTypes.Contains(DataIdTypes.Batch) && actionsSent > 0 ? 1 : 0;
                    }

                    if (options.AutoClearEnabledOnSend
                        && options.IdTrackingStyle == TrackingStyle.TrackEnabled)
                    {
                        Assert.AreEqual(newActionIds, state.DataIdsByType.TryGetValue(DataIdTypes.Action, out var actionIds) ? actionIds.Count : 0);
                        Assert.AreEqual(newCardIds, state.DataIdsByType.TryGetValue(DataIdTypes.Card, out var cardIds) ? cardIds.Count : 0);
                        Assert.AreEqual(newCarouselIds, state.DataIdsByType.TryGetValue(DataIdTypes.Carousel, out var carouselIds) ? carouselIds.Count : 0);
                        Assert.AreEqual(newBatchIds, state.DataIdsByType.TryGetValue(DataIdTypes.Batch, out var batchIds) ? batchIds.Count : 0);
                    }
                }
            })
                .Send(USERSAYS_TEXTPLEASE)
                    .AssertReply(activity => Assert.IsNull(((Activity)activity).Attachments))
                .Send(USERSAYS_ATTACHMENTSPLEASE)
                    .AssertReply(activity => Assert.AreEqual(2, ((Activity)activity).Attachments.Count))
                .Send(USERSAYS_TEXTPLEASE)
                    .AssertReply(activity => Assert.IsNull(((Activity)activity).Attachments))
                .Send(USERSAYS_BOTHPLEASE)
                    .AssertReply(activity => Assert.AreEqual(3, ((Activity)activity).Attachments.Count))
                .Send(new Activity(value: new object()))
                .StartTestAsync();
        }

        private static CardManagerMiddlewareOptions ReverseMiddlewareOptions(CardManagerMiddlewareOptions options) => new CardManagerMiddlewareOptions
        {
            AutoAdaptOutgoingCardActions = !options.AutoAdaptOutgoingCardActions,
            AutoApplyIds = !options.AutoApplyIds,
            AutoClearEnabledOnSend = !options.AutoClearEnabledOnSend,
            AutoConvertAdaptiveCards = !options.AutoConvertAdaptiveCards,
            AutoDeleteOnAction = !options.AutoDeleteOnAction,
            AutoDisableOnAction = !options.AutoDisableOnAction,
            AutoEnableOnSend = !options.AutoEnableOnSend,
            AutoSaveActivitiesOnSend = !options.AutoSaveActivitiesOnSend,
            AutoSeparateAttachmentsOnSend = !options.AutoSeparateAttachmentsOnSend,
            IdTrackingStyle = options.IdTrackingStyle == TrackingStyle.TrackEnabled ? TrackingStyle.TrackDisabled : TrackingStyle.TrackEnabled,
            IdOptions = ReverseIdOptions(options.IdOptions),
        };

        private static DataIdOptions ReverseIdOptions(DataIdOptions idOptions)
        {
            var hashSet = new HashSet<string>(DataId.Types);

            hashSet.ExceptWith(idOptions.GetIdTypes());

            return new DataIdOptions(hashSet);
        }
    }
}
