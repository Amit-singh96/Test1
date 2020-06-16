﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bot.Builder.Community.Cards.Translation
{
    public class AdaptiveCardTranslator
    {
        private const string HOST = "https://api.cognitive.microsofttranslator.com";
        private static readonly Lazy<HttpClient> _lazyClient = new Lazy<HttpClient>();

        public AdaptiveCardTranslator(IConfiguration configuration)
        {
            TranslatorKey = configuration[nameof(TranslatorKey)];
        }

        public static AdaptiveCardTranslatorSettings DefaultSettings => new AdaptiveCardTranslatorSettings
        {
            PropertiesToTranslate = new[] { "text", "altText", "fallbackText", "displayText", "title", "placeholder", "data" },
        };

        public AdaptiveCardTranslatorSettings Settings { get; set; } = DefaultSettings;

        public string TargetLocale { get; set; }

        /// <summary>
        /// Sets the key used for the Cognitive Services translator API.
        /// </summary>
        /// <value>
        /// The key used for the Cognitive Services translator API.
        /// </value>
        public string TranslatorKey { internal get; set; }

        public static async Task<T> TranslateAsync<T>(
            T card,
            string targetLocale,
            string translatorKey,
            AdaptiveCardTranslatorSettings settings = null,
            CancellationToken cancellationToken = default)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            if (string.IsNullOrEmpty(targetLocale))
            {
                throw new ArgumentNullException(nameof(targetLocale));
            }

            if (string.IsNullOrEmpty(translatorKey))
            {
                throw new ArgumentNullException(nameof(translatorKey));
            }

            return await TranslateAsync(
                card,
                async (inputs, innerCancellationToken) =>
                {
                    // From Cognitive Services translation documentation:
                    // https://docs.microsoft.com/en-us/azure/cognitive-services/translator/quickstart-csharp-translate
                    var requestBody = JsonConvert.SerializeObject(inputs.Select(input => new { Text = input }));

                    using (var request = new HttpRequestMessage())
                    {
                        var uri = $"{HOST}/translate?api-version=3.0&to={targetLocale}";
                        request.Method = HttpMethod.Post;
                        request.RequestUri = new Uri(uri);
                        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                        request.Headers.Add("Ocp-Apim-Subscription-Key", translatorKey);

                        var response = await _lazyClient.Value.SendAsync(request, innerCancellationToken).ConfigureAwait(false);

                        response.EnsureSuccessStatusCode();

                        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var result = JsonConvert.DeserializeObject<TranslatorResponse[]>(responseBody);

                        return result.Select(translatorResponse => translatorResponse?.Translations?.FirstOrDefault()?.Text).ToList();
                    }
                },
                settings,
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task<T> TranslateAsync<T>(
            T card,
            TranslateOneDelegate translateOneAsync,
            AdaptiveCardTranslatorSettings settings = null,
            CancellationToken cancellationToken = default)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            if (translateOneAsync is null)
            {
                throw new ArgumentNullException(nameof(translateOneAsync));
            }

            return await TranslateAsync(
                card,
                async (inputs, innerCancellationToken) =>
                {
                    var tasks = inputs.Select(async input => await translateOneAsync(input, innerCancellationToken).ConfigureAwait(false));
                    return await Task.WhenAll(tasks).ConfigureAwait(false);
                },
                settings,
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task<T> TranslateAsync<T>(
            T card,
            TranslateManyDelegate translateManyAsync,
            AdaptiveCardTranslatorSettings settings = null,
            CancellationToken cancellationToken = default)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            if (translateManyAsync is null)
            {
                throw new ArgumentNullException(nameof(translateManyAsync));
            }

            var cardJObject = card.ToJObject(true) ?? throw new ArgumentException(
                    "The Adaptive Card must be convertible to a JObject.",
                    nameof(card));

            var tokens = GetTokensToTranslate(cardJObject, settings ?? DefaultSettings);
            var translations = await translateManyAsync(
                tokens.Select(Convert.ToString).ToList(),
                cancellationToken).ConfigureAwait(false);

            if (translations != null)
            {
                for (int i = 0; i < tokens.Count && i < translations.Count; i++)
                {
                    var item = tokens[i];
                    var translatedText = translations[i];

                    if (!string.IsNullOrWhiteSpace(translatedText))
                    {
                        // Modify each stored JToken with the translated text
                        item.Replace(translatedText);
                    }
                }
            }

            return card.FromJObject(cardJObject);
        }

        public async Task<T> TranslateAsync<T>(T card, CancellationToken cancellationToken = default)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            return await TranslateAsync(card, TargetLocale, TranslatorKey, Settings, cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> TranslateAsync<T>(T card, string targetLocale, CancellationToken cancellationToken = default)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            return await TranslateAsync(card, targetLocale, TranslatorKey, Settings, cancellationToken).ConfigureAwait(false);
        }

        private static List<JToken> GetTokensToTranslate(JObject cardJObject, AdaptiveCardTranslatorSettings settings)
        {
            var tokens = new List<JToken>();

            // Find potential strings to translate
            foreach (var token in cardJObject.Descendants().Where(token => token.Type == JTokenType.String))
            {
                var parent = token.Parent;

                if (parent != null)
                {
                    var shouldTranslate = false;
                    var container = parent.Parent;

                    switch (parent.Type)
                    {
                        // If the string is the value of a property...
                        case JTokenType.Property:

                            var propertyName = (parent as JProperty).Name;

                            // container is assumed to be a JObject because it's the parent of a JProperty in this case
                            if (settings.PropertiesToTranslate?.Contains(propertyName) == true
                                || (propertyName == "value" && IsValueTranslatable(container as JObject)))
                            {
                                shouldTranslate = true;
                            }

                            break;

                        // If the string is in an array...
                        case JTokenType.Array:

                            if (IsArrayElementTranslatable(container))
                            {
                                shouldTranslate = true;
                            }

                            break;
                    }

                    if (shouldTranslate)
                    {
                        tokens.Add(token);
                    }
                }
            }

            return tokens;
        }

        private static bool IsArrayElementTranslatable(JContainer arrayContainer) => (arrayContainer as JProperty)?.Name == "inlines";

        private static bool IsValueTranslatable(JObject valueContainer)
        {
            if (valueContainer is null)
            {
                return false;
            }

            var elementType = valueContainer["type"];
            var parent = valueContainer.Parent;
            var grandparent = parent?.Parent;

            // value should be translated in facts, imBack (for MS Teams), and Input.Text,
            // and ignored in Input.Date and Input.Time and Input.Toggle and Input.ChoiceSet and Input.Choice
            return (elementType?.Type == JTokenType.String
                    && elementType.IsOneOf("Input.Text", "imBack"))
                || (elementType == null
                    && (grandparent as JProperty)?.Name == "facts"
                    && parent.Type == JTokenType.Array);
        }
    }
}
