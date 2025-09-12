using SendGrid;
using SendGrid.Helpers.Mail;

namespace FantasyToolbox.Services
{
    public interface IEmailService
    {
        Task<bool> SendVerificationEmailAsync(string toEmail, string firstName, string verificationCode);
    }

    public class EmailService : IEmailService
    {
        private readonly string _apiKey;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailService(IConfiguration configuration)
        {
            _apiKey = configuration["SendGrid:ApiKey"] ?? "";
            _fromEmail = configuration["SendGrid:FromEmail"] ?? "noreply@fantasytoolbox.com";
            _fromName = configuration["SendGrid:FromName"] ?? "FantasyToolbox";
        }

        public async Task<bool> SendVerificationEmailAsync(string toEmail, string firstName, string verificationCode)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                // In development, log the verification code instead of sending email
                Console.WriteLine($"[EMAIL DEBUG] Verification code for {toEmail}: {verificationCode}");
                return true;
            }

            try
            {
                var client = new SendGridClient(_apiKey);
                var from = new EmailAddress(_fromEmail, _fromName);
                var to = new EmailAddress(toEmail, firstName);
                var subject = "Verify Your Email Address - FantasyToolbox";

                var htmlContent = $@"
                    <h2>Welcome to FantasyToolbox!</h2>
                    <p>Hi {firstName},</p>
                    <p>Thank you for registering with FantasyToolbox. To complete your registration, please verify your email address by entering the following verification code in the app:</p>
                    <div style='font-size: 24px; font-weight: bold; background-color: #f0f0f0; padding: 15px; text-align: center; margin: 20px 0; border-radius: 5px;'>
                        {verificationCode}
                    </div>
                    <p>This verification code will expire in 15 minutes.</p>
                    <p>If you didn't create an account with FantasyToolbox, please ignore this email.</p>
                    <p>Best regards,<br>The FantasyToolbox Team</p>
                ";

                var plainTextContent = $@"
                    Welcome to FantasyToolbox!
                    
                    Hi {firstName},
                    
                    Thank you for registering with FantasyToolbox. To complete your registration, please verify your email address by entering the following verification code in the app:
                    
                    {verificationCode}
                    
                    This verification code will expire in 15 minutes.
                    
                    If you didn't create an account with FantasyToolbox, please ignore this email.
                    
                    Best regards,
                    The FantasyToolbox Team
                ";

                var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
                var response = await client.SendEmailAsync(msg);

                return response.StatusCode == System.Net.HttpStatusCode.Accepted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                return false;
            }
        }
    }
}