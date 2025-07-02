// using System;
// using System.Threading;
// using System.Threading.Tasks;
// using Xunit;
// using A2A;

// namespace A2A.AspNetCore.Tests;

// // These become A2AProcessor tests
// public class ProcessMessageTests
// {
//     [Fact]
//     public async Task ProcessMessage_SendAndGetTask_Works()
//     {
//         var taskManager = new TaskManager();
//         var taskId = Guid.NewGuid().ToString();
//         var sendParams = new TaskSendParams
//         {
//             Id = taskId,
//             Message = new Message
//             {
//                 Parts = [ new TextPart { Text = "Hello, World!" } ]
//             }
//         };
//         var sendRequest = new JsonRpcRequest
//         {
//             Id = Guid.NewGuid().ToString(),
//             Method = A2AMethods.TaskSend,
//             Params = sendParams
//         };
//         var sendResponse = await taskManager.ProcessMessageAsync(sendRequest, CancellationToken.None);
//         Assert.IsType<JsonRpcResponse>(sendResponse);
//         var sendResult = ((JsonRpcResponse)sendResponse).Result as AgentTask;
//         Assert.NotNull(sendResult);
//         Assert.Equal(taskId, sendResult.Id);
//         Assert.Equal(TaskState.Submitted, sendResult.Status.State);

//         var getRequest = new JsonRpcRequest
//         {
//             Id = Guid.NewGuid().ToString(),
//             Method = A2AMethods.TaskGet,
//             Params = new TaskIdParams { Id = taskId }
//         };
//         var getResponse = await taskManager.ProcessMessageAsync(getRequest, CancellationToken.None);
//         Assert.IsType<JsonRpcResponse>(getResponse);
//         var getResult = ((JsonRpcResponse)getResponse).Result as AgentTask;
//         Assert.NotNull(getResult);
//         Assert.Equal(taskId, getResult.Id);
//         Assert.Equal(TaskState.Submitted, getResult.Status.State);
//     }

//     [Fact]
//     public async Task ProcessMessage_CancelTask_Works()
//     {
//         var taskManager = new TaskManager();
//         var taskId = Guid.NewGuid().ToString();
//         var sendParams = new TaskSendParams
//         {
//             Id = taskId,
//             Message = new Message
//             {
//                 Parts = [ new TextPart { Text = "Hello, World!" } ]
//             }
//         };
//         var sendRequest = new JsonRpcRequest
//         {
//             Id = Guid.NewGuid().ToString(),
//             Method = A2AMethods.TaskSend,
//             Params = sendParams
//         };
//         await taskManager.ProcessMessageAsync(sendRequest, CancellationToken.None);

//         var cancelRequest = new JsonRpcRequest
//         {
//             Id = Guid.NewGuid().ToString(),
//             Method = A2AMethods.TaskCancel,
//             Params = new TaskIdParams { Id = taskId }
//         };
//         var cancelResponse = await taskManager.ProcessMessageAsync(cancelRequest, CancellationToken.None);
//         Assert.IsType<JsonRpcResponse>(cancelResponse);
//         var cancelResult = ((JsonRpcResponse)cancelResponse).Result as AgentTask;
//         Assert.NotNull(cancelResult);
//         Assert.Equal(taskId, cancelResult.Id);
//         Assert.Equal(TaskState.Canceled, cancelResult.Status.State);
// }
//     [Fact]
//     public async Task ProcessMessage_SetPushNotification_Works()
//     {
//         var taskManager = new TaskManager();
//         var taskId = Guid.NewGuid().ToString();
//         var pushNotificationConfig = new TaskPushNotificationConfig
//         {
//             Id = taskId,
//             PushNotificationConfig = new PushNotificationConfig
//             {
//                 Url = "http://example.com/notify",
//             }
//         };
//         var setRequest = new JsonRpcRequest
//         {
//             Id = Guid.NewGuid().ToString(),
//             Method = A2AMethods.TaskPushNotificationConfigSet,
//             Params = pushNotificationConfig
//         };
//         var setResponse = await taskManager.ProcessMessageAsync(setRequest, CancellationToken.None);
//         Assert.IsType<JsonRpcResponse>(setResponse);
//         var setResult = ((JsonRpcResponse)setResponse).Result as TaskPushNotificationConfig;
//         Assert.NotNull(setResult);
//         Assert.Equal(taskId, setResult.Id);
//         Assert.Equal("http://example.com/notify", setResult.PushNotificationConfig.Url);
//     }
// }
