using System.Net;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.Common;

/// <summary>
/// Builds the Guardian-facing emails. Kept in the Application layer (no provider
/// types involved) so the exact wording and the values a message carries can be
/// asserted in tests. Colours match the shared design system.
/// </summary>
public static class GuardianEmails
{
    private const string Primary = "#1E3A5F";
    private const string PrimaryLight = "#EAF0F7";
    private const string Text = "#14213D";
    private const string TextMuted = "#5B6B82";
    private const string Border = "#E1E6ED";
    private const string Background = "#F4F6F8";

    public static EmailMessage BuildCredentialEmail(string fullName, string toEmail, string contactNumber, string initialPassword)
    {
        const string subject = "Your ChildGuard guardian account";

        var text =
            $"""
            Hello {fullName},

            A ChildGuard guardian account has been created for you.

            Contact number (your login ID): {contactNumber}
            Initial password: {initialPassword}

            How to sign in for the first time:
            1. Open the ChildGuard guardian app and enter the contact number and initial password above.
            2. We will send a one-time password (OTP) to you. Enter it to continue.
            3. You will then be asked to set your own password. Choose one with at least 8 characters,
               including an uppercase letter and a digit.
            4. Sign in again with your new password. The initial password stops working at that point.

            If you did not expect this email, please contact your school administrator.

            ChildGuard
            """;

        var html =
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /></head>
            <body style="margin:0;padding:24px;background:{Background};font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Text};">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:560px;margin:0 auto;background:#FFFFFF;border:1px solid {Border};border-radius:10px;">
                <tr>
                  <td style="background:{Primary};padding:20px 24px;border-radius:10px 10px 0 0;">
                    <span style="color:#FFFFFF;font-size:20px;font-weight:700;">ChildGuard</span>
                  </td>
                </tr>
                <tr>
                  <td style="padding:24px;">
                    <p style="margin:0 0 16px;font-size:16px;">Hello {Encode(fullName)},</p>
                    <p style="margin:0 0 20px;font-size:15px;line-height:1.55;">
                      A ChildGuard guardian account has been created for you. Use the details below to sign in for the first time.
                    </p>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:{PrimaryLight};border-radius:8px;">
                      <tr>
                        <td style="padding:16px 18px;font-size:15px;">
                          <div style="color:{TextMuted};font-size:13px;">Contact number (your login ID)</div>
                          <div style="font-weight:600;font-size:16px;padding-bottom:12px;">{Encode(contactNumber)}</div>
                          <div style="color:{TextMuted};font-size:13px;">Initial password</div>
                          <div style="font-weight:600;font-size:16px;font-family:Consolas,Menlo,monospace;">{Encode(initialPassword)}</div>
                        </td>
                      </tr>
                    </table>
                    <p style="margin:20px 0 8px;font-size:15px;font-weight:600;">Signing in for the first time</p>
                    <ol style="margin:0 0 20px;padding-left:20px;font-size:15px;line-height:1.6;">
                      <li>Open the ChildGuard guardian app and enter the contact number and initial password above.</li>
                      <li>We will send you a one-time password (OTP). Enter it to continue.</li>
                      <li>Set your own password — at least 8 characters, with an uppercase letter and a digit.</li>
                      <li>Sign in again with your new password. The initial password stops working at that point.</li>
                    </ol>
                    <p style="margin:0;font-size:13px;color:{TextMuted};line-height:1.55;">
                      If you did not expect this email, please contact your school administrator.
                    </p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(toEmail, fullName, subject, html, text);
    }

    public static EmailMessage BuildLoginOtpEmail(string fullName, string toEmail, string code, TimeSpan validFor)
    {
        var minutes = (int)Math.Round(validFor.TotalMinutes);
        const string subject = "Your ChildGuard sign-in code";

        var text =
            $"""
            Hello {fullName},

            Your ChildGuard sign-in code is {code}.

            It expires in {minutes} minute(s) and can be used once.
            If you did not try to sign in, ignore this email and tell your school administrator.

            ChildGuard
            """;

        var html =
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /></head>
            <body style="margin:0;padding:24px;background:{Background};font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Text};">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:480px;margin:0 auto;background:#FFFFFF;border:1px solid {Border};border-radius:10px;">
                <tr>
                  <td style="background:{Primary};padding:20px 24px;border-radius:10px 10px 0 0;">
                    <span style="color:#FFFFFF;font-size:20px;font-weight:700;">ChildGuard</span>
                  </td>
                </tr>
                <tr>
                  <td style="padding:24px;text-align:center;">
                    <p style="margin:0 0 16px;font-size:15px;text-align:left;">Hello {Encode(fullName)},</p>
                    <p style="margin:0 0 16px;font-size:15px;text-align:left;">Use this code to finish signing in:</p>
                    <div style="background:{PrimaryLight};border-radius:8px;padding:18px;font-size:30px;font-weight:700;letter-spacing:6px;font-family:Consolas,Menlo,monospace;">{Encode(code)}</div>
                    <p style="margin:16px 0 0;font-size:13px;color:{TextMuted};text-align:left;line-height:1.55;">
                      The code expires in {minutes} minute(s) and can be used once.
                      If you did not try to sign in, ignore this email and tell your school administrator.
                    </p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(toEmail, fullName, subject, html, text);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
