﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;

namespace Bot.Builder.Community.Cards.Management.Tree
{
    internal static class CardTree
    {
        private const string SpecifyManually = " Try specifying the node type manually instead of using null.";

        private static readonly Dictionary<string, TreeNodeType> _cardTypes = new Dictionary<string, TreeNodeType>(StringComparer.OrdinalIgnoreCase)
        {
            { ContentTypes.AdaptiveCard, TreeNodeType.AdaptiveCard },
            { AnimationCard.ContentType, TreeNodeType.AnimationCard },
            { AudioCard.ContentType, TreeNodeType.AudioCard },
            { HeroCard.ContentType, TreeNodeType.HeroCard },
            { ReceiptCard.ContentType, TreeNodeType.ReceiptCard },
            { SigninCard.ContentType, TreeNodeType.SigninCard },
            { OAuthCard.ContentType, TreeNodeType.OAuthCard },
            { ThumbnailCard.ContentType, TreeNodeType.ThumbnailCard },
            { VideoCard.ContentType, TreeNodeType.VideoCard },
        };

        private static readonly Dictionary<TreeNodeType, ITreeNode> _tree = new Dictionary<TreeNodeType, ITreeNode>
        {
            {
                TreeNodeType.Batch, new EnumerableTreeNode<IMessageActivity>(TreeNodeType.Activity, DataIdScopes.Batch)
            },
            {
                TreeNodeType.Activity, new TreeNode<IMessageActivity, IEnumerable<Attachment>>((activity, next) =>
                {
                    // The nextAsync return value is not needed here because the Attachments property reference will remain unchanged
                    next(activity.Attachments, TreeNodeType.Carousel);

                    return activity;
                })
            },
            {
                TreeNodeType.Carousel, new EnumerableTreeNode<Attachment>(TreeNodeType.Attachment, DataIdScopes.Carousel)
            },
            {
                TreeNodeType.Attachment, new TreeNode<Attachment, object>((attachment, next) =>
                {
                    var contentType = attachment.ContentType;

                    if (contentType != null && _cardTypes.ContainsKey(contentType))
                    {
                        // The nextAsync return value is needed here because the attachment could be an Adaptive Card
                        // which would mean a new object was generated by the JObject conversion/deconversion
                        attachment.Content = next(attachment.Content, _cardTypes[contentType]);
                    }

                    return attachment;
                })
            },
            {
                TreeNodeType.AdaptiveCard, new TreeNode<object, IEnumerable<object>>((card, next) =>
                {
                    // Return the new object after it's been converted to a JObject and back
                    // so that the attachment node can assign it back to the Content property
                    return card.ToJObjectAndBack(
                        cardJObject =>
                        {
                            next(
                                AdaptiveCardUtil.NonDataDescendants(cardJObject)
                                    .Select(token => token is JObject element
                                            && element.GetValue(AdaptiveProperties.Type) is JToken type
                                            && type.Type == JTokenType.String
                                            && type.ToString().Equals(AdaptiveActionTypes.Submit)
                                        ? element : null)
                                    .WhereNotNull(), TreeNodeType.SubmitActionList);
                        }, true);
                })
            },
            {
                TreeNodeType.AnimationCard, new RichCardTreeNode<AnimationCard>(card => card.Buttons)
            },
            {
                TreeNodeType.AudioCard, new RichCardTreeNode<AudioCard>(card => card.Buttons)
            },
            {
                TreeNodeType.HeroCard, new RichCardTreeNode<HeroCard>(card => card.Buttons)
            },
            {
                TreeNodeType.OAuthCard, new RichCardTreeNode<OAuthCard>(card => card.Buttons)
            },
            {
                TreeNodeType.ReceiptCard, new RichCardTreeNode<ReceiptCard>(card => card.Buttons)
            },
            {
                TreeNodeType.SigninCard, new RichCardTreeNode<SigninCard>(card => card.Buttons)
            },
            {
                TreeNodeType.ThumbnailCard, new RichCardTreeNode<ThumbnailCard>(card => card.Buttons)
            },
            {
                TreeNodeType.VideoCard, new RichCardTreeNode<VideoCard>(card => card.Buttons)
            },
            {
                TreeNodeType.SubmitActionList, new EnumerableTreeNode<object>(TreeNodeType.SubmitAction, DataIdScopes.Card)
            },
            {
                TreeNodeType.CardActionList, new EnumerableTreeNode<CardAction>(TreeNodeType.CardAction, DataIdScopes.Card)
            },
            {
                TreeNodeType.SubmitAction, new TreeNode<object, object>((action, next, reassignChildren) =>
                {
                    // If the entry point was the Adaptive Card or higher
                    // then the action will already be a JObject
                    return action.ToJObjectAndBack(
                        actionJObject =>
                        {
                            // We need to create a "data" object in the submit action
                            // if there isn't one already
                            if (reassignChildren && actionJObject[AdaptiveProperties.Data].IsNullish())
                            {
                                actionJObject[AdaptiveProperties.Data] = new JObject();
                            }

                            if (actionJObject[AdaptiveProperties.Data] is JObject data)
                            {
                                next(data, TreeNodeType.ActionData);
                            }
                        }, true);
                })
            },
            {
                TreeNodeType.CardAction, new TreeNode<CardAction, object>((action, next, reassignChildren) =>
                {
                    if (action.Type == ActionTypes.MessageBack || action.Type == ActionTypes.PostBack)
                    {
                        if (action.Value.ToJObject(true) is JObject valueJObject)
                        {
                            next(valueJObject, TreeNodeType.ActionData);

                            if (reassignChildren)
                            {
                                action.Value = action.Value.FromJObject(valueJObject, true);
                            }
                        }
                        else
                        {
                            action.Text = action.Text.ToJObjectAndBack(
                                jObject =>
                                {
                                    next(jObject, TreeNodeType.ActionData);
                                },
                                true);
                        }
                    }

                    return action;
                })
            },
            {
                TreeNodeType.ActionData, new TreeNode<object, object>((data, next, reassignChildren) =>
                {
                    return data.ToJObjectAndBack(jObject =>
                    {
                        // We need to create a library data object in the action data
                        // if there isn't one already
                        if (reassignChildren && jObject[PropertyNames.LibraryData].IsNullish())
                        {
                            jObject[PropertyNames.LibraryData] = new JObject();
                        }

                        next(jObject[PropertyNames.LibraryData], TreeNodeType.LibraryData);
                    });
                })
            },
            {
                TreeNodeType.LibraryData, new TreeNode<object, DataId>((data, next) =>
                {
                    return data.ToJObjectAndBack(jObject =>
                    {
                        foreach (var scope in DataId.Scopes)
                        {
                            var id = jObject[scope]?.ToString();

                            if (id != null)
                            {
                                next(new DataId(scope, id), TreeNodeType.Id);
                            }
                        }
                    });
                })
            },
            {
                TreeNodeType.Id, new TreeNode<DataId, object>()
            },
        };

        /// <summary>
        /// Enters and exits the tree at the specified nodes.
        /// </summary>
        /// <typeparam name="TEntry">The .NET type of the entry node.</typeparam>
        /// <typeparam name="TExit">The .NET type of the exit node.</typeparam>
        /// <param name="entryValue">The entry value.</param>
        /// <param name="action">A delegate to execute on each exit value
        /// that is expected to return that value or a new object.
        /// Note that the argument is guaranteed to be non-null.</param>
        /// <param name="entryType">The explicit position of the entry node in the tree.
        /// If this is null then the position is inferred from the TEntry type parameter.
        /// Note that this parameter is required if the type is <see cref="object"/>
        /// or if the position otherwise cannot be unambiguously inferred from the type.</param>
        /// <param name="exitType">The explicit position of the exit node in the tree.
        /// If this is null then the position is inferred from the TExit type parameter.
        /// Note that this parameter is required if the type is <see cref="object"/>
        /// or if the position otherwise cannot be unambiguously inferred from the type.</param>
        /// <param name="reassignChildren">True if each child should be reassigned to its parent during recursion
        /// (which breaks Adaptive Card attachment content references when they get converted to a
        /// <see cref="JObject"/> and back), false if each original reference should remain.</param>
        /// <param name="processIntermediateNode">A delegate to execute on each node during recursion.</param>
        /// <returns>The possibly-modified entry value. This is needed if a new object was created
        /// to modify the value, such as when an Adaptive Card is converted to a <see cref="JObject"/>.</returns>
        internal static TEntry Recurse<TEntry, TExit>(
                TEntry entryValue,
                Action<TExit> action,
                TreeNodeType? entryType = null,
                TreeNodeType? exitType = null,
                bool reassignChildren = false,
                Action<ITreeNode> processIntermediateNode = null)
            where TEntry : class
            where TExit : class
        {
            ITreeNode entryNode = null;
            ITreeNode exitNode = null;

            try
            {
                entryNode = GetNode<TEntry>(entryType);
            }
            catch (Exception ex)
            {
                throw GetNodeArgumentException<TEntry>(ex);
            }

            try
            {
                exitNode = GetNode<TExit>(exitType);
            }
            catch (Exception ex)
            {
                throw GetNodeArgumentException<TExit>(ex, "exit");
            }

            object Next(object child, TreeNodeType childType)
            {
                var childNode = _tree[childType];
                var modifiedChild = child;

                if (childNode == exitNode)
                {
                    if (GetExitValue<TExit>(child) is TExit typedChild)
                    {
                        action(typedChild);
                    }
                }
                else
                {
                    processIntermediateNode?.Invoke(childNode);

                    modifiedChild = childNode.CallChild(child, Next, reassignChildren);
                }

                return reassignChildren ? modifiedChild : child;
            }

            processIntermediateNode?.Invoke(entryNode);

            return entryNode.CallChild(entryValue, Next, reassignChildren) as TEntry;
        }

        internal static void SetLibraryData<TEntry>(TEntry entryValue, object data, TreeNodeType? entryType = null, bool merge = false)
            where TEntry : class
        {
            var dataJObject = data.ToJObject(true);

            if (dataJObject == null && data != null)
            {
                throw new ArgumentException(
                    "The data is not an appropriate type or is serialized incorrectly.",
                    nameof(data));
            }

            var action = merge
                ? new Action<JObject>(libraryData => libraryData.Merge(dataJObject))
                : new Action<JObject>(libraryData => libraryData.Replace(dataJObject));

            Recurse(
                entryValue,
                action,
                entryType,
                TreeNodeType.LibraryData,
                true);
        }

        internal static void ApplyIds<TEntry>(TEntry entryValue, DataIdOptions options = null, TreeNodeType? entryType = null)
            where TEntry : class
        {
            options = options ?? new DataIdOptions(DataIdScopes.Action);

            var modifiedOptions = options.Clone();

            Recurse(
                entryValue,
                (JObject data) =>
                {
                    data.ApplyIdsToActionData(modifiedOptions);
                },
                entryType,
                TreeNodeType.ActionData,
                true,
                node =>
                {
                    if (node.IdScope is string idScope)
                    {
                        if (options.HasIdScope(idScope))
                        {
                            var id = options.Get(idScope);

                            if (id is null)
                            {
                                modifiedOptions.Set(idScope, DataId.GenerateValue(idScope));
                            }
                        }
                    }
                });
        }

        internal static ISet<DataId> GetIds<TEntry>(TEntry entryValue, TreeNodeType? entryType = null)
            where TEntry : class
        {
            var ids = new HashSet<DataId>();

            Recurse(
                entryValue,
                (DataId dataId) =>
                {
                    ids.Add(dataId);
                }, entryType);

            return ids;
        }

        private static TExit GetExitValue<TExit>(object child)
                where TExit : class
            => child is JToken jToken && !typeof(JToken).IsAssignableFrom(typeof(TExit)) ? jToken.ToObject<TExit>() : child as TExit;

        // TODO: Require explicit node types and stop checking types at runtime
        private static ITreeNode GetNode<T>(TreeNodeType? nodeType)
        {
            var t = typeof(T);

            if (nodeType is null)
            {
                if (t == typeof(object))
                {
                    throw new Exception("A node cannot be automatically determined from a System.Object type argument." + SpecifyManually);
                }

                var matchingNodes = new List<ITreeNode>();

                foreach (var possibleNode in _tree.Values)
                {
                    var possibleNodeTValue = possibleNode.GetTValue();

                    if (possibleNodeTValue.IsAssignableFrom(t) &&
                        possibleNodeTValue != typeof(object) &&
                        possibleNodeTValue != typeof(IEnumerable<object>))
                    {
                        matchingNodes.Add(possibleNode);
                    }
                }

                var count = matchingNodes.Count();

                if (count < 1)
                {
                    throw new Exception($"No node exists that's assignable from the type argument: {t}. Try using a different type.");
                }

                if (count > 1)
                {
                    throw new Exception($"Multiple nodes exist that are assignable from the type argument: {t}." + SpecifyManually);
                }

                return matchingNodes.First();
            }

            var exactNode = _tree[nodeType.Value];

            return exactNode.GetTValue().IsAssignableFrom(t)
                ? exactNode
                : throw new Exception($"The node type {nodeType} is not assignable from the type argument: {t}."
                    + " Make sure you're providing the correct node type.");
        }

        private static ArgumentException GetNodeArgumentException<TEntry>(Exception inner, string entryOrExit = "entry")
        {
            return new ArgumentException(
                $"The {entryOrExit} node could not be determined from the type argument: {typeof(TEntry)}.",
                $"{entryOrExit}Type",
                inner);
        }
    }
}
