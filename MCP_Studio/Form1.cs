using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

using MCP_Studio.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

using OpenAI.Chat;
using System.Text;
using ModelContextProtocol.Protocol.Types;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure;

namespace MCP_Studio
{
    public partial class Form1 : Form
    {
        //OpenAI
        private ChatClient _chatClient;
        private List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private string _modelId;
        private OpenAiConfig aiConfig;
        //MCP
        private List<McpClientWrapper> _mcpClients = new List<McpClientWrapper>();
        private McpClientWrapper _mcpClient;
        private IList<McpClientTool> _tools;
        private List<ChatTool> _openAiTools;
        private string _configPath = "mcp_config.json";
        //
        private bool _isProcessingRequest = false;
        private readonly object _lockObject = new object();

        public Form1()
        {
            InitializeComponent();
            InitializeOpenAI();
        }

        private void InitializeOpenAI()
        {
            try
            {
                // JSON ���Ͽ��� OpenAI API ������ �о�ɴϴ�.
                aiConfig = LoadOpenAiConfig("config.json");              
                // Azure OpenAI ���ҽ� ��������Ʈ �� API Ű
                var endpoint = new Uri(aiConfig.Endpoint);
                var apiKey = aiConfig.ApiKey; // ���� API Ű�� ��ü�ϼ���
                var deploymentName = aiConfig.DeploymentId; // ������ ���� �̸�

                // AzureOpenAIClient �ʱ�ȭ
                var azureClient = new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));
                _chatClient = azureClient.GetChatClient(deploymentName);

                AppendToChat("System", "OpenAI Ŭ���̾�Ʈ �ʱ�ȭ �Ϸ�");
            }
            catch (FileNotFoundException ex)
            {
                HandleError("���� ������ ã�� �� �����ϴ�.", ex);
            }
            catch (JsonException ex)
            {
                HandleError("���� ���� ������ �߸��Ǿ����ϴ�.", ex);
            }
            catch (Exception ex)
            {
                HandleError("OpenAI �ʱ�ȭ �� ������ �߻��߽��ϴ�.", ex);
            }
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await InitializeMcpClientAsync();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // ���ҽ� ����
            try
            {
                // _mcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                LogError("MCP Ŭ���̾�Ʈ ���� �� ������ �߻��߽��ϴ�.", ex);
            }

            base.OnFormClosing(e);
        }
        #region �̺�Ʈ �Լ�
        private async void button1_Click(object sender, EventArgs e)
        {
            if (_isProcessingRequest)
            {
                MessageBox.Show("���� ��û�� ó�� ���Դϴ�. ��� ��ٷ��ּ���.", "ó�� ��", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _isProcessingRequest = true;
                SetStatus("ó�� ��...");
                await HandleChatAsync();
            }
            catch (Exception ex)
            {
                HandleError("ä�� ó�� �� ������ �߻��߽��ϴ�.", ex);
            }
            finally
            {
                _isProcessingRequest = false;
                SetStatus("�غ��");
            }
        }
        private async void richTextBox1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (_isProcessingRequest)
                {
                    MessageBox.Show("���� ��û�� ó�� ���Դϴ�. ��� ��ٷ��ּ���.", "ó�� ��", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    _isProcessingRequest = true;
                    SetStatus("ó�� ��...");
                    await HandleChatAsync();
                }
                catch (Exception ex)
                {
                    HandleError("ä�� ó�� �� ������ �߻��߽��ϴ�.", ex);
                }
                finally
                {
                    _isProcessingRequest = false;
                    SetStatus("�غ��");
                }
            }
        }
        private async void button2_Click(object sender, EventArgs e)
        {
            try
            {
                string toolName = "echo"; // �˻��� ���� �̸�

                var clientWithTool = await FindClientByToolNameAsync(toolName);

                if (clientWithTool != null)
                {
                    // ������ �����ϴ� Ŭ���̾�Ʈ �߰�
                    MessageBox.Show($"���� '{toolName}'��(��) '{clientWithTool.ServerName}' ������ �ֽ��ϴ�.", "���� ã�� ���");

                    // �ʿ��ϴٸ� �ش� Ŭ���̾�Ʈ�� Ȱ�� Ŭ���̾�Ʈ�� ����
                    _mcpClient = clientWithTool;

                    // UI ������Ʈ �� �߰� �۾�...
                }
                else
                {
                    MessageBox.Show($"���� '{toolName}'��(��) ���� ������ ã�� �� �����ϴ�.", "���� ã�� ���", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                HandleError("���� ����� �ҷ����� �� ������ �߻��߽��ϴ�.", ex);
            }
        }
        private async void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox1.SelectedIndex >= 0 && listBox1.SelectedIndex < _mcpClients.Count)
            {
                McpClientWrapper mcpClient = _mcpClients[listBox1.SelectedIndex];

                // ��� ������ ���� ��� ��������
                var tools = await mcpClient.ListToolsAsync();

                richTextBox3.Clear();

                richTextBox3.AppendText($"����: {mcpClient.ServerName}\n");
                richTextBox3.AppendText($"���� ��: {tools.Count}\n\n");

                foreach (var tool in tools)
                {
                    richTextBox3.AppendText($"���� �̸�: {tool.Name}\n");
                    richTextBox3.AppendText($"����: {tool.Description}\n");
                    richTextBox3.AppendText("-----------------------------------\n");
                }

            }
        }
        #endregion

        #region �Ϲ� �Լ�
        private string ConvertToWindowsPath(string unixPath)
        {
            if (string.IsNullOrEmpty(unixPath))
                return unixPath;

            // /Users/username/path ������ C:\Users\username\path �������� ��ȯ �õ�
            if (unixPath.StartsWith("/Users/"))
            {
                string relativePath = unixPath.Substring("/Users/".Length);
                string[] parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                {
                    // ����� �̸��� �����Ͽ� Windows ��� ����
                    string username = parts[0];
                    string winPath = $@"C:\Users\{username}";

                    // ������ ��� �߰�
                    for (int i = 1; i < parts.Length; i++)
                    {
                        winPath = Path.Combine(winPath, parts[i]);
                    }

                    return winPath;
                }
            }

            // �ܼ� ��ȯ: �����ø� �齽���÷� ����
            return unixPath.Replace('/', '\\');
        }
        private void AppendToChat(string sender, string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendToChat(sender, message)));
                return;
            }

            richTextBox2.AppendText($"{sender}: {message}\n\n");
            richTextBox2.ScrollToCaret();
        }
        private void SetStatus(string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatus(status)));
                return;
            }
            toolStripStatusLabel1.Text = status;
        }
        private void HandleError(string message, Exception ex)
        {
            string errorDetails = ex != null ? $"\n\n�� ����: {ex.Message}" : "";
            AppendToChat("Error", message + errorDetails);

            // �߿� ������ �޽��� �ڽ��ε� ǥ��
            if (ex != null && !(ex is FileNotFoundException || ex is JsonException))
            {
                MessageBox.Show(message + errorDetails, "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // �α� ���Ͽ� ���� ���
            LogError(message, ex);
        }
        private void LogError(string message, Exception ex)
        {
            try
            {
                string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, $"error_{DateTime.Now:yyyyMMdd}.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

                if (ex != null)
                {
                    logEntry += $"\nException: {ex.GetType().Name}\nMessage: {ex.Message}\nStackTrace: {ex.StackTrace}";
                }

                File.AppendAllText(logFile, logEntry + "\n\n");
            }
            catch
            {
                // �α� �� ���� �߻� �� ���� (�α� ���а� ���ø����̼� ���࿡ ������ ���� �ʵ���)
            }
        }
        private OpenAiConfig LoadOpenAiConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"���� ������ ã�� �� �����ϴ�: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<OpenAiConfig>(json);

            if (string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ModelID))
            {
                throw new InvalidOperationException("���� ���Ͽ� �ʼ� ����(ApiKey �Ǵ� ModelID)�� �����Ǿ����ϴ�.");
            }

            return config;
        }
        private void UpdateMcpConfigForWindows()
        {
            try
            {
                string configPath = "mcp_config.json";
                string json = File.ReadAllText(configPath);
                JObject config = JObject.Parse(json);

                var servers = config["mcpServers"];
                if (servers != null)
                {
                    // filesystem ���� ���� Ȯ��
                    var fsServer = servers["filesystem"];
                    if (fsServer != null)
                    {
                        var args = fsServer["args"] as JArray;
                        if (args != null && args.Count > 0)
                        {
                            // ������ ���ڰ� ����� ��� Windows ȣȯ�ǰ� ����
                            string lastArg = args[args.Count - 1].ToString();
                            if (lastArg.StartsWith("/"))
                            {
                                string winPath = ConvertToWindowsPath(lastArg);
                                args[args.Count - 1] = winPath;

                                // ����� ���� ����
                                File.WriteAllText(configPath, config.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MCP ���� ���� ���� ����: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region MCP �Լ�
        private async Task InitializeMcpClientAsync()
        {
            try
            {
                // ���� mcpClient ����
                if (_mcpClient != null)
                {
                    await _mcpClient.DisposeAsync();
                    _mcpClient = null;
                }

                // ���� ����� ��� Ŭ���̾�Ʈ ����
                foreach (var client in _mcpClients)
                {
                    await client.DisposeAsync();
                }
                _mcpClients.Clear();

                SetStatus("MCP ���� �ʱ�ȭ ��...");
                richTextBox1.Enabled = false;

                // ���� Ŭ���̾�Ʈ ����
                await CleanupMcpClientsAsync();
                // ��� ���� ���� �ε�
                var servers = McpConfigLoader.LoadAllMcpServers(_configPath);
                if (servers.Count == 0)
                {
                    toolStripStatusLabel1.Text = "������ MCP ������ �����ϴ�.";
                    return;
                }
                // ���¹� ������Ʈ
                toolStripStatusLabel1.Text = $"MCP ���� {servers.Count}���� �ҷ����� ��...";
                // ����Ʈ�ڽ� �ʱ�ȭ
                listBox1.Items.Clear();

                // �� ������ ���� Ŭ���̾�Ʈ ����
                foreach (var server in servers)
                {
                    var mcpClient = new McpClientWrapper();
                    try
                    {
                        // ���� ��� ���� �� ���� (Windows ȯ���� ���)
                        if (server.Name == "filesystem" && server.Args.Length > 0)
                        {
                            // ������ ���ڰ� ������� Ȯ��
                            string lastArg = server.Args[server.Args.Length - 1];
                            if (lastArg.StartsWith("/") && !Directory.Exists(lastArg))
                            {
                                // Unix/Mac ��Ÿ�� ��θ� Windows ��η� ��ȯ �õ�
                                string winPath = ConvertToWindowsPath(lastArg);
                                if (Directory.Exists(winPath))
                                {
                                    // �迭�� ������ ��� ����
                                    server.Args[server.Args.Length - 1] = winPath;
                                }
                                else
                                {
                                    // ��ΰ� ���� ��� ���� ���丮�� ���
                                    server.Args[server.Args.Length - 1] = Directory.GetCurrentDirectory();
                                }
                            }
                        }
                        bool success = await mcpClient.InitializeAsync(server.Command, server.Args, server.Name);
                        _mcpClients.Add(mcpClient);
                        if (success)
                        {
                            // ����Ʈ�ڽ��� ���� �̸� �߰�
                            listBox1.Items.Add(server.Name);
                            if (mcpClient != null)
                            {
                                // ���� ���� ��� ǥ��
                                await DisplayServerToolsAsync(mcpClient);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await mcpClient.DisposeAsync();
                        toolStripStatusLabel1.Text = $"MCP ���� '{server.Name}' ���� ����: {ex.Message}";
                    }
                }

                //_mcpClient = new McpClientWrapper();
                //var (command, args) = McpConfigLoader.LoadMcpServerConfig("mcp_config.json", "everything");

                //await _mcpClient.InitializeAsync(command, args);

                AppendToChat("System", "MCP Ŭ���̾�Ʈ �ʱ�ȭ �Ϸ�");




                richTextBox1.Enabled = true;
                SetStatus("�غ��");
            }
            catch (FileNotFoundException ex)
            {
                HandleError("MCP ���� ������ ã�� �� �����ϴ�.", ex);
            }
            catch (JsonException ex)
            {
                HandleError("MCP ���� ���� ������ �߸��Ǿ����ϴ�.", ex);
            }
            catch (Exception ex)
            {
                HandleError("MCP Ŭ���̾�Ʈ �ʱ�ȭ ����", ex);
            }
        }
        private async Task CleanupMcpClientsAsync()
        {
            // ���� mcpClient ����
            if (_mcpClient != null)
            {
                await _mcpClient.DisposeAsync();
                _mcpClient = null;
            }

            // ���� ����� ��� Ŭ���̾�Ʈ ����
            foreach (var client in _mcpClients)
            {
                await client.DisposeAsync();
            }
            _mcpClients.Clear();
        }
        private async Task DisplayServerToolsAsync(McpClientWrapper client)
        {
            try
            {
                // ��� ������ ���� ��� ��������
                var tools = await client.ListToolsAsync();

                AppendToChat("System", $"{tools.Count}���� ������ ã�ҽ��ϴ�.");

                // ���� ����� ���������� �������� �Ŀ��� ��ȯ �۾� ����
                if (tools != null && tools.Count > 0)
                {
                    // _openAiTools�� null�� ��� �ʱ�ȭ
                    if (_openAiTools == null)
                    {
                        _openAiTools = new List<ChatTool>();
                    }
                    // ���� ������ ������ OpenAI �������� ��ȯ
                    var convertedTools = ConvertMcpToolsToOpenAiTools(tools);
                    AppendToChat("System", $"{convertedTools.Count}���� ������ OpenAI �������� ��ȯ�߽��ϴ�.");

                    // �ߺ� ����: �̹� �ִ� ������ �ǳʶٰ� �� ������ �߰�
                    foreach (var tool in convertedTools)
                    {
                        // ���� �̸��� ������ �̹� �ִ��� Ȯ��
                        if (!_openAiTools.Any(t => t.FunctionName == tool.FunctionName))
                        {
                            _openAiTools.Add(tool);
                            AppendToChat("System", $"���� �߰���: {tool.FunctionName} (����: {client.ServerName})");
                        }
                        else
                        {
                            AppendToChat("System", $"���� �ߺ� ���õ�: {tool.FunctionName} (�̹� �ٸ� �������� �߰���)");
                        }
                    }                   
                    AppendToChat("System", $"�� {_openAiTools.Count}���� OpenAI ������ ��ϵǾ����ϴ�.");
                }
                else
                {
                    AppendToChat("System", "��� ������ ������ �����ϴ�.");
                    toolStripStatusLabel1.Text = "���� ����";
                }


            }
            catch (Exception ex)
            {
                richTextBox3.Text = $"���� ����� �������� �� ���� �߻�: {ex.Message}";
            }
        }
        /// <summary>
        /// ������ ���� �̸��� ���� ������ �����ϴ� McpClientWrapper�� ã���ϴ�.
        /// </summary>
        /// <param name="toolName">ã�� ������ �̸�</param>
        /// <returns>������ �����ϴ� McpClientWrapper, ������ null</returns>
        private async Task<McpClientWrapper> FindClientByToolNameAsync(string toolName)
        {
            if (string.IsNullOrEmpty(toolName) || _mcpClients == null || _mcpClients.Count == 0)
            {
                return null;
            }

            foreach (var client in _mcpClients)
            {
                try
                {
                    // �� Ŭ���̾�Ʈ���� ���� ��� ��������
                    var tools = await client.ListToolsAsync();

                    // ���� �̸��� ��ġ�ϴ��� Ȯ��
                    if (tools.Any(tool => tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return client;
                    }
                }
                catch (Exception ex)
                {
                    // ���� �α� (���� �ڵ忡���� �α� ��Ŀ���� ���)
                    Debug.WriteLine($"Ŭ���̾�Ʈ {client.ServerName}���� ���� �˻� �� ���� �߻�: {ex.Message}");
                    continue; // ������ �־ ���� Ŭ���̾�Ʈ Ȯ��
                }
            }

            // ��ġ�ϴ� ������ ã�� ���� ���
            return null;
        }
        // MCP ������ OpenAI �������� ��ȯ�ϴ� �Լ� �߰�
        private List<ChatTool> ConvertMcpToolsToOpenAiTools(IList<McpClientTool> mcpTools)
        {
            var openAiTools = new List<ChatTool>();

            if (mcpTools == null || mcpTools.Count == 0)
            {
                AppendToChat("System", "��� ������ ������ �����ϴ�.");
                return null;
            }
            // MCP ������ OpenAI ���� ��ȯ�մϴ�.
            openAiTools = mcpTools.Select(tool =>
            {
                try
                {
                    // MCP ������ �Է� ��Ű���� JSON ���ڿ��� ��ȯ�մϴ�.
                    string rawJsonSchema = tool.ProtocolTool.InputSchema.GetRawText();

                    // JSON ���ڿ��� BinaryData�� ��ȯ�մϴ�.
                    var parameters = BinaryData.FromString(rawJsonSchema);

                    // OpenAI ���� �����մϴ�.
                    return ChatTool.CreateFunctionTool(
                        functionName: tool.Name,
                        functionDescription: tool.Description,
                        functionParameters: parameters
                    );
                }
                catch (Exception ex)
                {
                    AppendToChat("System", $"���� {tool.Name} ��ȯ ����: {ex.Message}");
                    return null;
                }
            })
            .Where(tool => tool != null)
            .ToList();

            return openAiTools;
        }
        #endregion

        #region GPT �Լ�
        private async Task HandleChatAsync()
        {
            // �Է� ��ȿ�� �˻�
            var userInput = richTextBox1.Text.Trim();
            if (string.IsNullOrEmpty(userInput))
            {
                MessageBox.Show("�޽����� �Է����ּ���.", "�Է� ����", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_chatClient == null)
            {
                HandleError("OpenAI Ŭ���̾�Ʈ�� �ʱ�ȭ���� �ʾҽ��ϴ�.", null);
                return;
            }

            AppendToChat("You", userInput);
            richTextBox1.Clear();

            _chatHistory.Add(new UserChatMessage(userInput));

            try
            {
                var options = CreateChatCompletionOptions();

                // �ʱ� AI ���� ��û
                var response = await _chatClient.CompleteChatAsync(_chatHistory, options);

                // ���� ȣ���� �ִ� ���
                if (response.Value.ToolCalls != null && response.Value.ToolCalls.Count > 0)
                {
                    await ProcessToolCallsAsync(response.Value.ToolCalls, options);
                }
                // �Ϲ� ������ ���
                else if (response.Value.Content != null && response.Value.Content.Count > 0)
                {
                    ProcessAssistantResponse(response);
                }
                else
                {
                    AppendToChat("System", "GPT ������ �����ϴ�.");
                }
            }
            catch (Exception ex)
            {
                _chatHistory.Add(new AssistantChatMessage("�˼��մϴ�. ������ ó���ϴ� ���� ������ �߻��߽��ϴ�."));
                HandleError("AI ���� ��û �� ������ �߻��߽��ϴ�.", ex);
            }
        }

        private ChatCompletionOptions CreateChatCompletionOptions()
        {
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1000,
                Temperature = 0.7f
            };

            if (_openAiTools != null && _openAiTools.Count > 0)
            {
                foreach (var tool in _openAiTools)
                {
                    options.Tools.Add(tool);
                }
            }

            return options;
        }

        private async Task ProcessToolCallsAsync(IReadOnlyList<object> toolCalls, ChatCompletionOptions options)
        {
            AppendToChat("System", $"{toolCalls.Count}���� ���� ȣ���� ó���մϴ�...");

            // ���� ������ ������ ����
            Dictionary<string, string> toolResponses = new Dictionary<string, string>();

            // ������ ����� �޽��� ã��
            string lastUserMessage = "";
            for (int i = _chatHistory.Count - 1; i >= 0; i--)
            {
                if (_chatHistory[i] is UserChatMessage userMessage)
                {
                    // ����� �޽����� ���� �������� (SDK�� ���� �Ӽ����� �ٸ� �� ����)
                    lastUserMessage = userMessage.Content.ToString();
                    break;
                }
            }

            // �� tool ȣ�⿡ ���� ���� ����
            foreach (var toolCall in toolCalls)
            {
                dynamic dynamicToolCall = toolCall;
                try
                {
                    string functionName = dynamicToolCall.FunctionName;
                    BinaryData argumentsBinaryData = dynamicToolCall.FunctionArguments;
                    string toolCallId = dynamicToolCall.Id;

                    string argumentsJson = argumentsBinaryData.ToString();
                    AppendToChat("System", $"���� ȣ��: {functionName}");
                    AppendToChat("System", $"����: {argumentsJson}");

                    var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(argumentsJson);

                    var mcpClient = await FindClientByToolNameAsync(functionName);

                    if (mcpClient == null)
                    {
                        AppendToChat("System", "MCP Ŭ���̾�Ʈ�� �ʱ�ȭ���� �ʾҽ��ϴ�.");
                        continue;
                    }

                    string mcpResponse = await mcpClient.CallToolAsync(functionName, parameters);
                    AppendToChat("System", $"���� ����: {mcpResponse}");

                    // ���� ���� ����
                    toolResponses[functionName] = mcpResponse;
                }
                catch (Exception ex)
                {
                    AppendToChat("System", $"���� ���� ����: {ex.Message}");
                }
            }

            try
            {
                // ������ ���ο� ��ȭ ����
                var newConversation = new List<ChatMessage>();

                // �ʱ� �ý��� �޽��� �߰� (���û���)
                newConversation.Add(new SystemChatMessage("������ ����� ������ ����� �����ص帳�ϴ�."));

                // ���� ����� ��û�� ���� ����� �ϳ��� �޽����� ����
                StringBuilder combinedMessage = new StringBuilder();
                combinedMessage.AppendLine($"���� ��û: {lastUserMessage}");
                combinedMessage.AppendLine();
                combinedMessage.AppendLine("���� ���� ���:");

                foreach (var response in toolResponses)
                {
                    combinedMessage.AppendLine($"- {response.Key}: {response.Value}");
                }

                combinedMessage.AppendLine();
                combinedMessage.AppendLine("�� ����� ����� �������ּ���.");

                newConversation.Add(new UserChatMessage(combinedMessage.ToString()));

                AppendToChat("System", "���ο� ��ȭ�� ������ ��û�մϴ�.");

                // ��� ���� �� �α� (������)
                AppendToChat("System", $"��� ���� ��: {aiConfig.DeploymentId}");

                // �� ��ȭ�� API ȣ��
                var finalResponse = await _chatClient.CompleteChatAsync(newConversation, options);

                AppendToChat("System", $"���� ����: {finalResponse.Value != null}");

                if (finalResponse.Value != null &&
                    finalResponse.Value.Content != null &&
                    finalResponse.Value.Content.Count > 0)
                {
                    var assistantReply = finalResponse.Value.Content[0].Text?.Trim();
                    if (!string.IsNullOrEmpty(assistantReply))
                    {
                        AppendToChat("ChatGPT", assistantReply);

                        // ���� ä�� �̷¿� ���� ���� �� ��ý���Ʈ ���� �߰�
                        // ���� ���� ����� ������ ����� �޽���
                        string toolResultsForHistory = "���� ���� ���:\n";
                        foreach (var response in toolResponses)
                        {
                            toolResultsForHistory += $"- {response.Key}: {response.Value}\n";
                        }

                        _chatHistory.Add(new UserChatMessage(toolResultsForHistory));
                        _chatHistory.Add(new AssistantChatMessage(assistantReply));
                    }
                    else
                    {
                        AppendToChat("System", "GPT ������ ��� �ֽ��ϴ�.");
                        // ��ü ���� �߰�
                        string fallbackReply = "���� ���� ����� Ȯ���߽��ϴ�.";
                        AppendToChat("ChatGPT", fallbackReply);
                        _chatHistory.Add(new AssistantChatMessage(fallbackReply));
                    }
                }
                else
                {
                    AppendToChat("System", "���� ȣ�� �� GPT ������ �����ϴ�.");
                    // ��ü ���� �߰�
                    string fallbackReply = "������ ����Ǿ����ϴ�.";
                    AppendToChat("ChatGPT", fallbackReply);
                    _chatHistory.Add(new AssistantChatMessage(fallbackReply));
                }
            }
            catch (Exception ex)
            {
                AppendToChat("System", $"���� ���� ��û ����: {ex.Message}");
                AppendToChat("System", $"���� Ʈ���̽�: {ex.StackTrace}");
                HandleError("���� ȣ�� �� ���� ���� ��û ����", ex);

                // ���� �߻��� ��ü ���� �߰�
                string errorReply = "���� ���� �� ������ �޴µ� ������ �߻��߽��ϴ�.";
                AppendToChat("ChatGPT", errorReply);
                _chatHistory.Add(new AssistantChatMessage(errorReply));
            }
        }

        private void ProcessAssistantResponse(System.ClientModel.ClientResult<ChatCompletion> response)
        {
            if (response.Value.Content != null && response.Value.Content.Count > 0)
            {
                var assistantReply = response.Value.Content[0].Text.Trim();
                AppendToChat("ChatGPT", assistantReply);

                // ä�� �̷¿� AI ���� �߰�
                _chatHistory.Add(new AssistantChatMessage(assistantReply));
            }
            else
            {
                AppendToChat("System", "GPT ������ �����ϴ�.");
            }
        }
        #endregion







    }
}