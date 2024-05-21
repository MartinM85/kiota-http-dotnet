﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Kiota.Http.HttpClientLibrary.Tests.Mocks;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Kiota.Http.HttpClientLibrary.Tests
{
    public class HttpClientRequestAdapterTests
    {
        private readonly IAuthenticationProvider _authenticationProvider;
        private readonly HttpClientRequestAdapter requestAdapter;

        public HttpClientRequestAdapterTests()
        {
            _authenticationProvider = new Mock<IAuthenticationProvider>().Object;
            requestAdapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        }

        [Fact]
        public void ThrowsArgumentNullExceptionOnNullAuthenticationProvider()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new HttpClientRequestAdapter(null));
            Assert.Equal("authenticationProvider", exception.ParamName);
        }

        [Fact]
        public void BaseUrlIsSetAsExpected()
        {
            var httpClientRequestAdapter = new HttpClientRequestAdapter(_authenticationProvider);
            Assert.Null(httpClientRequestAdapter.BaseUrl);// url is null

            httpClientRequestAdapter.BaseUrl = "https://graph.microsoft.com/v1.0";
            Assert.Equal("https://graph.microsoft.com/v1.0", httpClientRequestAdapter.BaseUrl);// url is set as expected

            httpClientRequestAdapter.BaseUrl = "https://graph.microsoft.com/v1.0/";
            Assert.Equal("https://graph.microsoft.com/v1.0", httpClientRequestAdapter.BaseUrl);// url is does not have the last `/` character
        }

        [Fact]
        public void BaseUrlIsSetFromHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            var httpClientRequestAdapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: httpClient);

            Assert.NotNull(httpClientRequestAdapter.BaseUrl);// url is not null
            Assert.Equal("https://graph.microsoft.com/v1.0", httpClientRequestAdapter.BaseUrl);// url is does not have the last `/` character
        }

        [Fact]
        public void EnablesBackingStore()
        {
            // Arrange
            var requestAdapter = new HttpClientRequestAdapter(_authenticationProvider);
            var backingStore = new Mock<IBackingStoreFactory>().Object;

            //Assert the that we originally have an in memory backing store
            Assert.IsAssignableFrom<InMemoryBackingStoreFactory>(BackingStoreFactorySingleton.Instance);

            // Act
            requestAdapter.EnableBackingStore(backingStore);

            //Assert the backing store has been updated
            Assert.IsAssignableFrom(backingStore.GetType(), BackingStoreFactorySingleton.Instance);
        }


        [Fact]
        public async Task GetRequestMessageFromRequestInformationWithBaseUrlTemplate()
        {
            // Arrange
            requestAdapter.BaseUrl = "http://localhost";
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "{+baseurl}/me"
            };

            // Act
            var requestMessage = await requestAdapter.ConvertToNativeRequestAsync<HttpRequestMessage>(requestInfo);

            // Assert
            Assert.NotNull(requestMessage.RequestUri);
            Assert.Contains("http://localhost/me", requestMessage.RequestUri.OriginalString);
        }

        [Fact]
        public async Task GetRequestMessageFromRequestInformationUsesBaseUrlFromAdapter()
        {
            // Arrange
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "{+baseurl}/me",
                PathParameters = new Dictionary<string, object>
                {
                    { "baseurl", "https://graph.microsoft.com/beta"}//request information with different base url
                }

            };
            // Change the baseUrl of the adapter
            requestAdapter.BaseUrl = "http://localhost";

            // Act
            var requestMessage = await requestAdapter.ConvertToNativeRequestAsync<HttpRequestMessage>(requestInfo);

            // Assert
            Assert.NotNull(requestMessage.RequestUri);
            Assert.Contains("http://localhost/me", requestMessage.RequestUri.OriginalString);// Request generated using adapter baseUrl
        }

        [Theory]
        [InlineData("select", new[] { "id", "displayName" }, "select=id,displayName")]
        [InlineData("count", true, "count=true")]
        [InlineData("skip", 10, "skip=10")]
        [InlineData("skip", null, "")]// query parameter no placed
        public async Task GetRequestMessageFromRequestInformationSetsQueryParametersCorrectlyWithSelect(string queryParam, object queryParamObject, string expectedString)
        {
            // Arrange
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "http://localhost/me{?top,skip,search,filter,count,orderby,select}"
            };
            requestInfo.QueryParameters.Add(queryParam, queryParamObject);

            // Act
            var requestMessage = await requestAdapter.ConvertToNativeRequestAsync<HttpRequestMessage>(requestInfo);

            // Assert
            Assert.NotNull(requestMessage.RequestUri);
            Assert.Contains(expectedString, requestMessage.RequestUri.Query);
        }

        [Fact]
        public async Task GetRequestMessageFromRequestInformationSetsContentHeaders()
        {
            // Arrange
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.PUT,
                UrlTemplate = "https://sn3302.up.1drv.com/up/fe6987415ace7X4e1eF866337"
            };
            requestInfo.Headers.Add("Content-Length", "26");
            requestInfo.Headers.Add("Content-Range", "bytes 0-25/128");
            requestInfo.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("contents")), "application/octet-stream");

            // Act
            var requestMessage = await requestAdapter.ConvertToNativeRequestAsync<HttpRequestMessage>(requestInfo);

            // Assert
            Assert.NotNull(requestMessage.Content);
            // Content length set correctly
            Assert.Equal(26, requestMessage.Content.Headers.ContentLength);
            // Content range set correctly
            Assert.Equal("bytes", requestMessage.Content.Headers.ContentRange.Unit);
            Assert.Equal(0, requestMessage.Content.Headers.ContentRange.From);
            Assert.Equal(25, requestMessage.Content.Headers.ContentRange.To);
            Assert.Equal(128, requestMessage.Content.Headers.ContentRange.Length);
            Assert.True(requestMessage.Content.Headers.ContentRange.HasRange);
            Assert.True(requestMessage.Content.Headers.ContentRange.HasLength);
            // Content type set correctly
            Assert.Equal("application/octet-stream", requestMessage.Content.Headers.ContentType.MediaType);
        }

        [Fact]
        public async Task SendMethodDoesNotThrowWithoutUrlTemplate()
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("Test")))
                });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                URI = new Uri("https://example.com")
            };

            var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo);

            Assert.True(response.CanRead);
            Assert.Equal(4, response.Length);
        }

        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
        [InlineData(HttpStatusCode.PartialContent)]
        [Theory]
        public async Task SendStreamReturnsUsableStream(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("Test")))
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo);

            Assert.True(response.CanRead);
            Assert.Equal(4, response.Length);
            var streamReader = new StreamReader(response);
            var responseString = await streamReader.ReadToEndAsync();
            Assert.Equal("Test", responseString);
        }
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
        [InlineData(HttpStatusCode.NoContent)]
        [Theory]
        public async Task SendStreamReturnsNullForNoContent(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo);

            Assert.Null(response);
        }
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
        [InlineData(HttpStatusCode.NoContent)]
        [InlineData(HttpStatusCode.PartialContent)]
        [Theory]
        public async Task SendSNoContentDoesntFailOnOtherStatusCodes(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            await adapter.SendNoContentAsync(requestInfo);
        }
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
        [InlineData(HttpStatusCode.NoContent)]
        [InlineData(HttpStatusCode.ResetContent)]
        [Theory]
        public async Task SendReturnsNullOnNoContent(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            var response = await adapter.SendAsync<MockEntity>(requestInfo, MockEntity.Factory);

            Assert.Null(response);
        }

        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
        [InlineData(HttpStatusCode.NoContent)]
        [InlineData(HttpStatusCode.ResetContent)]
        [Theory]
        public async Task SendReturnsNullOnNoContentWithContentHeaderPresent(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            var response = await adapter.SendAsync<MockEntity>(requestInfo, MockEntity.Factory);

            Assert.Null(response);
        }
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
        [Theory]
        public async Task SendReturnsObjectOnContent(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            using var mockContent = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("Test")));
            mockContent.Headers.ContentType = new("application/json");
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = mockContent,
            });
            var mockParseNode = new Mock<IParseNode>();
            mockParseNode.Setup<IParsable>(x => x.GetObjectValue(It.IsAny<ParsableFactory<MockEntity>>()))
            .Returns(new MockEntity());
            var mockParseNodeFactory = new Mock<IAsyncParseNodeFactory>();
            mockParseNodeFactory.Setup(x => x.GetRootParseNodeAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(mockParseNode.Object));
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client, parseNodeFactory: mockParseNodeFactory.Object);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            var response = await adapter.SendAsync<MockEntity>(requestInfo, MockEntity.Factory);

            Assert.NotNull(response);
        }
        [Fact]
        public async Task RetriesOnCAEResponse()
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            var methodCalled = false;
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((mess, token) =>
            {
                var response = new HttpResponseMessage
                {
                    StatusCode = methodCalled ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                    Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("Test")))
                };
                if(!methodCalled)
                    response.Headers.WwwAuthenticate.Add(new("Bearer", "realm=\"\", authorization_uri=\"https://login.microsoftonline.com/common/oauth2/authorize\", client_id=\"00000003-0000-0000-c000-000000000000\", error=\"insufficient_claims\", claims=\"eyJhY2Nlc3NfdG9rZW4iOnsibmJmIjp7ImVzc2VudGlhbCI6dHJ1ZSwgInZhbHVlIjoiMTY1MjgxMzUwOCJ9fX0=\""));
                methodCalled = true;
                return Task.FromResult(response);
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };

            var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo);

            Assert.NotNull(response);

            mockHandler.Protected().Verify("SendAsync", Times.Exactly(2), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.BadGateway)]
        [Theory]
        public async Task SetsTheApiExceptionStatusCode(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var responseMessage = new HttpResponseMessage
                {
                    StatusCode = statusCode
                };
                responseMessage.Headers.Add("request-id", "guid-value");
                return responseMessage;
            });
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };
            try
            {
                var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo);
                Assert.Fail("Expected an ApiException to be thrown");
            }
            catch(ApiException e)
            {
                Assert.Equal((int)statusCode, e.ResponseStatusCode);
                Assert.True(e.ResponseHeaders.ContainsKey("request-id"));
            }
        }
        [InlineData(HttpStatusCode.NotFound)]// 4XX
        [InlineData(HttpStatusCode.BadGateway)]// 5XX
        [Theory]
        public async Task SelectsTheXXXErrorMappingClassCorrectly(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var responseMessage = new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                return responseMessage;
            });
            var mockParseNode = new Mock<IParseNode>();
            mockParseNode.Setup<IParsable>(x => x.GetObjectValue(It.IsAny<ParsableFactory<IParsable>>()))
            .Returns(new MockError("A general error occured"));
            var mockParseNodeFactory = new Mock<IAsyncParseNodeFactory>();
            mockParseNodeFactory.Setup(x => x.GetRootParseNodeAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(mockParseNode.Object));
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, mockParseNodeFactory.Object, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };
            try
            {
                var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>()
                {
                    { "XXX", (parseNode) => new MockError("A general error occured")},
                };
                var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo, errorMapping);
                Assert.Fail("Expected an ApiException to be thrown");
            }
            catch(MockError mockError)
            {
                Assert.Equal((int)statusCode, mockError.ResponseStatusCode);
                Assert.Equal("A general error occured", mockError.Message);
            }
        }
        [InlineData(HttpStatusCode.BadGateway)]// 5XX
        [Theory]
        public async Task ThrowsApiExceptionOnMissingMapping(HttpStatusCode statusCode)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            var client = new HttpClient(mockHandler.Object);
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var responseMessage = new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                return responseMessage;
            });
            var mockParseNode = new Mock<IParseNode>();
            mockParseNode.Setup<IParsable>(x => x.GetObjectValue(It.IsAny<ParsableFactory<IParsable>>()))
            .Returns(new MockError("A general error occured: " + statusCode.ToString()));
            var mockParseNodeFactory = new Mock<IAsyncParseNodeFactory>();
            mockParseNodeFactory.Setup(x => x.GetRootParseNodeAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(mockParseNode.Object));
            var adapter = new HttpClientRequestAdapter(_authenticationProvider, mockParseNodeFactory.Object, httpClient: client);
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "https://example.com"
            };
            try
            {
                var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>()
                {
                    { "4XX", (parseNode) => new MockError("A 4XX error occured") }//Only 4XX
                };
                var response = await adapter.SendPrimitiveAsync<Stream>(requestInfo, errorMapping);
                Assert.Fail("Expected an ApiException to be thrown");
            }
            catch(ApiException apiException)
            {
                Assert.Equal((int)statusCode, apiException.ResponseStatusCode);
                Assert.Contains("The server returned an unexpected status code and no error factory is registered for this code", apiException.Message);
            }
        }
    }
}
