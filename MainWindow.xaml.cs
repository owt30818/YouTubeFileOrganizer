using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace FileOrganizer
{
    public partial class MainWindow : Window
    {
        private string sourceFolderPath = string.Empty;
        private string targetFolderPath = string.Empty;
        private CancellationTokenSource? cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectSourceFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "원본 폴더를 선택하세요";

            if (dialog.ShowDialog() == true)
            {
                sourceFolderPath = dialog.FolderName;
                SourceFolderTextBox.Text = sourceFolderPath;
                UpdateStartButtonState();
            }
        }

        private void SelectTargetFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "대상 폴더를 선택하세요";

            if (dialog.ShowDialog() == true)
            {
                targetFolderPath = dialog.FolderName;
                TargetFolderTextBox.Text = targetFolderPath;
                UpdateStartButtonState();
            }
        }

        private void UpdateStartButtonState()
        {
            StartButton.IsEnabled = !string.IsNullOrEmpty(sourceFolderPath) && 
                                   !string.IsNullOrEmpty(targetFolderPath);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 취소 토큰 생성
                cancellationTokenSource = new CancellationTokenSource();
                
                StartButton.IsEnabled = false;
                CancelButton.IsEnabled = true;
                LogTextBlock.Text = string.Empty;
                ProgressBar.Value = 0;
                ProgressTextBlock.Text = "0/0";

                bool isMovingFiles = MoveModeRadio.IsChecked == true;
                string operationMode = isMovingFiles ? "이동" : "복사";

                AddLog("파일 정리를 시작합니다...");
                AddLog($"원본 폴더: {sourceFolderPath}");
                AddLog($"대상 폴더: {targetFolderPath}");
                AddLog($"작업 모드: {operationMode}");
                AddLog("");

                await OrganizeFiles(isMovingFiles, cancellationTokenSource.Token);

                AddLog("");
                AddLog("파일 정리가 완료되었습니다.");
            }
            catch (OperationCanceledException)
            {
                AddLog("");
                AddLog("파일 정리가 취소되었습니다.");
            }
            catch (Exception ex)
            {
                AddLog($"오류 발생: {ex.Message}");
                System.Windows.MessageBox.Show($"오류가 발생했습니다: {ex.Message}", "오류", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            AddLog("취소 요청됨...");
        }

        private async Task OrganizeFiles(bool isMovingFiles, CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(sourceFolderPath, "*", SearchOption.TopDirectoryOnly);
            
            ProgressBar.Maximum = files.Length;
            ProgressTextBlock.Text = $"0/{files.Length}";

            int processedCount = 0;

            foreach (string filePath in files)
            {
                // 취소 확인
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    
                    // 선택된 날짜 기준에 따라 날짜 결정
                    DateTime targetDate;
                    string dateType;
                    
                    if (CreationDateRadio.IsChecked == true)
                    {
                        targetDate = fileInfo.CreationTime.Date;
                        dateType = "생성일";
                    }
                    else if (ModifiedDateRadio.IsChecked == true)
                    {
                        targetDate = fileInfo.LastWriteTime.Date;
                        dateType = "수정일";
                    }
                    else // AccessDateRadio.IsChecked == true
                    {
                        targetDate = fileInfo.LastAccessTime.Date;
                        dateType = "접근일";
                    }
                    
                    var folderName = targetDate.ToString("yyyy-MM-dd");
                    
                    // 디버그 정보 추가
                    AddLog($"파일: {fileInfo.Name}");
                    AddLog($"  {dateType}: {targetDate:yyyy-MM-dd} (원본: {(CreationDateRadio.IsChecked == true ? fileInfo.CreationTime : ModifiedDateRadio.IsChecked == true ? fileInfo.LastWriteTime : fileInfo.LastAccessTime):yyyy-MM-dd HH:mm:ss})");
                    AddLog($"  폴더명: {folderName}");
                    
                    var targetFolder = Path.Combine(targetFolderPath, folderName);
                    
                    // 대상 폴더가 없으면 생성
                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                        AddLog($"  → 폴더 생성: {folderName}");
                    }
                    else
                    {
                        AddLog($"  → 기존 폴더 사용: {folderName}");
                    }

                    var targetFilePath = Path.Combine(targetFolder, fileInfo.Name);
                    
                    // 파일명 중복 처리
                    targetFilePath = GetUniqueFilePath(targetFilePath);

                    if (isMovingFiles)
                    {
                        // 파일 이동
                        File.Move(filePath, targetFilePath);
                        AddLog($"  → 이동 완료: {Path.GetFileName(targetFilePath)}");
                    }
                    else
                    {
                        // 파일 복사
                        File.Copy(filePath, targetFilePath);
                        AddLog($"  → 복사 완료: {Path.GetFileName(targetFilePath)}");
                        
                        // 복사 검증 (복사 모드에서만)
                        bool verificationPassed = await VerifyFileCopy(originalPath: filePath, copiedPath: targetFilePath, cancellationToken);
                        if (verificationPassed)
                        {
                            AddLog($"  ✓ 검증 성공");
                        }
                        else
                        {
                            AddLog($"  ✗ 검증 실패");
                            File.Delete(targetFilePath); // 검증 실패 시 복사본 삭제
                            throw new Exception("파일 복사 검증에 실패했습니다.");
                        }
                    }

                    AddLog(""); // 빈 줄 추가

                    processedCount++;
                    ProgressBar.Value = processedCount;
                    ProgressTextBlock.Text = $"{processedCount}/{files.Length}";

                    // UI 업데이트를 위한 지연
                    await Task.Delay(10, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw; // 취소 예외는 다시 던짐
                }
                catch (Exception ex)
                {
                    AddLog($"파일 처리 실패: {Path.GetFileName(filePath)} - {ex.Message}");
                    AddLog(""); // 빈 줄 추가
                }
            }
        }

        private async Task<bool> VerifyFileCopy(string originalPath, string copiedPath, CancellationToken cancellationToken)
        {
            if (!VerifyFileSizeCheckBox.IsChecked == true && !VerifyHashCheckBox.IsChecked == true)
            {
                return true; // 검증 옵션이 모두 꺼져있으면 항상 성공
            }

            try
            {
                // 취소 확인
                cancellationToken.ThrowIfCancellationRequested();
                
                var originalInfo = new FileInfo(originalPath);
                var copiedInfo = new FileInfo(copiedPath);

                // 파일 크기 검증
                if (VerifyFileSizeCheckBox.IsChecked == true)
                {
                    if (originalInfo.Length != copiedInfo.Length)
                    {
                        AddLog($"    파일 크기 불일치: 원본={originalInfo.Length:N0} bytes, 복사본={copiedInfo.Length:N0} bytes");
                        return false;
                    }
                    AddLog($"    파일 크기 일치: {originalInfo.Length:N0} bytes");
                }

                // MD5 해시 검증
                if (VerifyHashCheckBox.IsChecked == true)
                {
                    AddLog($"    MD5 해시 계산 중...");
                    
                    string originalHash = await CalculateMD5Hash(originalPath, cancellationToken);
                    string copiedHash = await CalculateMD5Hash(copiedPath, cancellationToken);

                    if (!string.Equals(originalHash, copiedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        AddLog($"    MD5 해시 불일치");
                        AddLog($"      원본: {originalHash}");
                        AddLog($"      복사본: {copiedHash}");
                        return false;
                    }
                    AddLog($"    MD5 해시 일치: {originalHash}");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw; // 취소 예외는 다시 던짐
            }
            catch (Exception ex)
            {
                AddLog($"    검증 중 오류 발생: {ex.Message}");
                return false;
            }
        }

        private async Task<string> CalculateMD5Hash(string filePath, CancellationToken cancellationToken)
        {
            using var md5 = MD5.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            
            var hashBytes = await Task.Run(() => md5.ComputeHash(stream), cancellationToken);
            return Convert.ToHexString(hashBytes);
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            
            int counter = 1;
            string newFilePath;
            
            do
            {
                newFilePath = Path.Combine(directory!, $"{fileName}_{counter}{extension}");
                counter++;
            }
            while (File.Exists(newFilePath));

            return newFilePath;
        }

        private void AddLog(string message)
        {
            LogTextBlock.Text += message + Environment.NewLine;
            
            // 자동 스크롤
            if (LogTextBlock.Parent is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToEnd();
            }
        }
    }
}