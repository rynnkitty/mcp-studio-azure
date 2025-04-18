using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCP_Studio
{
    public partial class ImageViewerForm : Form
    {
        public ImageViewerForm()
        {
            InitializeComponent();
        }
        // Base64 문자열로부터 이미지 표시
        public void LoadBase64Image(string base64String)
        {
            try
            {
                // Base64 문자열에서 "data:image/jpeg;base64," 등의 헤더가 있으면 제거
                if (base64String.Contains(","))
                {
                    base64String = base64String.Split(',')[1];
                }

                // Base64 문자열을 바이트 배열로 변환
                byte[] imageBytes = Convert.FromBase64String(base64String);

                // 바이트 배열을 메모리 스트림으로 변환
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    // 메모리 스트림에서 이미지 생성
                    pictureBox.Image = Image.FromStream(ms);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이미지 로드 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
