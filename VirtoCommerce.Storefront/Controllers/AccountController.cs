using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VirtoCommerce.Storefront.AutoRestClients.PlatformModuleApi;
using VirtoCommerce.Storefront.AutoRestClients.PlatformModuleApi.Models;
using VirtoCommerce.Storefront.Domain;
using VirtoCommerce.Storefront.Domain.Common;
using VirtoCommerce.Storefront.Domain.Security;
using VirtoCommerce.Storefront.Domain.Security.Notifications;
using VirtoCommerce.Storefront.Extensions;
using VirtoCommerce.Storefront.Infrastructure;
using VirtoCommerce.Storefront.Model;
using VirtoCommerce.Storefront.Model.Common;
using VirtoCommerce.Storefront.Model.Common.Events;
using VirtoCommerce.Storefront.Model.Common.Notifications;
using VirtoCommerce.Storefront.Model.Security;
using VirtoCommerce.Storefront.Model.Security.Events;
using VirtoCommerce.Storefront.Model.Security.Specifications;

namespace VirtoCommerce.Storefront.Controllers
{
    [StorefrontRoute("account")]
    public class AccountController : StorefrontControllerBase
    {
        private readonly SignInManager<User> _signInManager;
        private readonly IEventPublisher _publisher;
        private readonly StorefrontOptions _options;
        private readonly INotifications _platformNotificationApi;
        private readonly IAuthorizationService _authorizationService;

        private readonly string[] _firstNameClaims = { ClaimTypes.GivenName, "urn:github:name", ClaimTypes.Name };

        public AccountController(
            IWorkContextAccessor workContextAccessor,
            IStorefrontUrlBuilder urlBuilder,
            SignInManager<User> signInManager,
            IEventPublisher publisher,
            INotifications platformNotificationApi,
            IAuthorizationService authorizationService,
            IOptions<StorefrontOptions> options)
            : base(workContextAccessor, urlBuilder)
        {
            _signInManager = signInManager;
            _publisher = publisher;
            _options = options.Value;
            _platformNotificationApi = platformNotificationApi;
            _authorizationService = authorizationService;
        }

        //GET: /account
        [HttpGet]
        public async Task<ActionResult> GetAccount()
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, OnlyRegisteredUserAuthorizationRequirement.PolicyName);
            if (!authorizationResult.Succeeded)
            {
                return new ChallengeResult();
            }

            //Customer should be already populated in WorkContext middle-ware
            return View("customers/account", WorkContext);
        }

        [HttpGet("order/{number}")]
        public async Task<ActionResult> GetOrderDetails(string number)
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, OnlyRegisteredUserAuthorizationRequirement.PolicyName);
            if (!authorizationResult.Succeeded)
            {
                return new ChallengeResult();
            }

            var order = WorkContext.CurrentUser?.Orders.FirstOrDefault(x => x.Number.EqualsInvariant(number));
            if (order != null)
            {
                WorkContext.CurrentOrder = order;
            }

            return View("customers/order", WorkContext);
        }

        [HttpGet("addresses")]
        public async Task<ActionResult> GetAddresses()
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, OnlyRegisteredUserAuthorizationRequirement.PolicyName);
            if (!authorizationResult.Succeeded)
            {
                return new ChallengeResult();
            }

            return View("customers/addresses", WorkContext);
        }

        [HttpGet("register")]
        [AllowAnonymous]
        public ActionResult Register()
        {
            return View("customers/register", WorkContext);
        }

        [HttpPost("register")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register([FromForm] UserRegistration registration)
        {
            TryValidateModel(registration);

            //This required for populate fields on form on post-back
            WorkContext.Form = Form.FromObject(registration);

            if (ModelState.IsValid)
            {
                //Register user
                var user = registration.ToUser();
                user.Contact = registration.ToContact();
                user.StoreId = WorkContext.CurrentStore.Id;

                var result = await _signInManager.UserManager.CreateAsync(user, registration.Password);
                if (result.Succeeded)
                {
                    user = await _signInManager.UserManager.FindByNameAsync(user.UserName);
                    await _publisher.Publish(new UserRegisteredEvent(WorkContext, user, registration));

                    await _signInManager.SignInAsync(user, isPersistent: true);
                    await _publisher.Publish(new UserLoginEvent(WorkContext, user));

                    //Send new user registration notification
                    var registrationEmailNotification = new RegistrationEmailNotification(WorkContext.CurrentStore.Id, WorkContext.CurrentLanguage)
                    {
                        FirstName = registration.FirstName,
                        LastName = registration.LastName,
                        Login = registration.UserName,
                        Sender = WorkContext.CurrentStore.Email,
                        Recipient = GetUserEmail(user)
                    };
                    await _platformNotificationApi.SendNotificationAsync(registrationEmailNotification.ToNotificationDto());

                    if (_options.SendAccountConfirmation)
                    {
                        var token = await _signInManager.UserManager.GenerateEmailConfirmationTokenAsync(user);
                        var callbackUrl = Url.Action("ConfirmEmail", "Account", new { UserId = user.Id, Token = token }, protocol: Request.Scheme, host: WorkContext.CurrentStore.Host);
                        var emailConfirmationNotification = new EmailConfirmationNotification(WorkContext.CurrentStore.Id, WorkContext.CurrentLanguage)
                        {
                            Url = callbackUrl,
                            Sender = WorkContext.CurrentStore.Email,
                            Recipient = GetUserEmail(user)
                        };
                        await _platformNotificationApi.SendNotificationAsync(emailConfirmationNotification.ToNotificationDto());
                    }

                    return StoreFrontRedirect("~/account");
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        WorkContext.Form.Errors.Add(new FormError { Code = error.Code.PascalToKebabCase(), Description = error.Description });
                    }
                }
            }

            return View("customers/register", WorkContext);
        }

        [HttpGet("confirminvitation")]
        [AllowAnonymous]
        public async Task<ActionResult> ConfirmInvitation(string organizationId, string email, string token)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.InvalidUrl());
                return View("error", WorkContext);
            }

            var user = await _signInManager.UserManager.FindByEmailAsync(email);
            if (user == null)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.UserNotFound());
                return View("error", WorkContext);
            }

            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.InvitationHasAreadyBeenUsed());
                return View("error", WorkContext);
            }

            var isValidToken = await _signInManager.UserManager.VerifyUserTokenAsync(user, _signInManager.UserManager.Options.Tokens.PasswordResetTokenProvider, UserManager<User>.ResetPasswordTokenPurpose, token);
            if (!isValidToken)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.InvalidToken());
                return View("error", WorkContext);
            }

            return View("customers/confirm_invitation", WorkContext);
        }

        [HttpPost("confirminvitation")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ConfirmInvitation([FromForm] UserRegistrationByInvitation register)
        {
            var result = IdentityResult.Success;
            TryValidateModel(register);

            if (ModelState.IsValid)
            {
                var user = await _signInManager.UserManager.FindByEmailAsync(register.Email);
                if (user != null)
                {
                    result = await _signInManager.UserManager.ResetPasswordAsync(user, register.Token, register.Password);
                    if (result.Succeeded)
                    {
                        user.UserName = register.UserName;
                        user.Contact = register.ToContact();
                        user.Contact.OrganizationId = register.OrganizationId;

                        result = await _signInManager.UserManager.UpdateAsync(user);
                        if (result.Succeeded)
                        {
                            await _publisher.Publish(new UserRegisteredEvent(WorkContext, user, register));
                            await _signInManager.SignInAsync(user, isPersistent: true);
                            await _publisher.Publish(new UserLoginEvent(WorkContext, user));
                        }


                        return StoreFrontRedirect("~/account");
                    }
                }
            }

            WorkContext.Form.Errors.AddRange(result.Errors.Select(x => new FormError { Code = x.Code.PascalToKebabCase(), Description = x.Description }));

            return View("customers/confirm_invitation", WorkContext);
        }

        [HttpGet("confirmemail")]
        [AllowAnonymous]
        public async Task<ActionResult> ConfirmEmail(string token)
        {
            var result = await _signInManager.UserManager.ConfirmEmailAsync(WorkContext.CurrentUser, token);
            var viewName = result.Succeeded ? "confirmation-done" : "error";
            return View(viewName);
        }

        [HttpGet("impersonate/{userId}")]
        public async Task<IActionResult> ImpersonateUser(string userId)
        {
            if (User.Identity.Name == SecurityConstants.AnonymousUsername)
            {
                return StoreFrontRedirect($"~/account/login?ReturnUrl={Request.Path}");
            }
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, null, CanImpersonateAuthorizationRequirement.PolicyName);
            if (!authorizationResult.Succeeded)
            {
                return Forbid();
            }
            var impersonateUser = await _signInManager.UserManager.FindByIdAsync(userId);
            if (impersonateUser != null)
            {
                impersonateUser.OperatorUserId = WorkContext.CurrentUser.Id;
                impersonateUser.OperatorUserName = WorkContext.CurrentUser.UserName;

                // sign out the current user
                await _signInManager.SignOutAsync();

                await _signInManager.SignInAsync(impersonateUser, isPersistent: false);
            }
            return StoreFrontRedirect("~/");
        }

        [HttpGet("login")]
        [AllowAnonymous]
        public ActionResult Login()
        {
            return View("customers/login");
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login([FromForm] Login login, string returnUrl)
        {
            TryValidateModel(login);

            //This required for populate fields on form on post-back
            WorkContext.Form = Form.FromObject(login);


            if (!ModelState.IsValid)
            {
                return View("customers/login", WorkContext);
            }
            login.UserName = login.UserName?.Trim();

            var loginResult = await _signInManager.PasswordSignInAsync(login.UserName, login.Password, login.RememberMe, lockoutOnFailure: true);

            if (loginResult.Succeeded)
            {
                var user = await _signInManager.UserManager.FindByNameAsync(login.UserName);

                //Check that current user can sing in to current store
                if (new CanUserLoginToStoreSpecification(user).IsSatisfiedBy(WorkContext.CurrentStore) && new IsUserSuspendedSpecification().IsSatisfiedBy(user) == false)
                {
                    await _publisher.Publish(new UserLoginEvent(WorkContext, user));
                    return StoreFrontRedirect(returnUrl);
                }
                else
                {
                    WorkContext.Form.Errors.Add(SecurityErrorDescriber.UserCannotLoginInStore());
                }
            }

            if (loginResult.IsLockedOut)
            {
                return View("lockedout", WorkContext);
            }

            if (loginResult.RequiresTwoFactor)
            {
                return StoreFrontRedirect("~/account/sendcode");
            }

            if (loginResult is CustomSignInResult signInResult && signInResult.IsRejected)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.AccountIsBlocked());
            }

            WorkContext.Form.Errors.Add(SecurityErrorDescriber.LoginFailed());

            return View("customers/login", WorkContext);
        }

        [HttpGet("logout")]
        [AllowAnonymous]
        public async Task<ActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return StoreFrontRedirect("~/");
        }

        [HttpGet("externallogin")]
        [AllowAnonymous]
        public ActionResult ExternalLogin(string authType, string returnUrl)
        {
            if (string.IsNullOrEmpty(authType))
            {
                return new BadRequestResult();
            }

            var properties = _signInManager.ConfigureExternalAuthenticationProperties(authType, Url.Action("ExternalLoginCallback", "Account", new { returnUrl }));
            return Challenge(properties, authType);
        }

        [HttpGet("externallogincallback")]
        [AllowAnonymous]
        public async Task<ActionResult> ExternalLoginCallback(string returnUrl)
        {
            var loginInfo = await _signInManager.GetExternalLoginInfoAsync();
            if (loginInfo == null)
            {
                return View("customers/login", WorkContext);
            }

            User user;

            // Sign in the user with this external login provider if the user already has a login.
            var externalLoginResult = await _signInManager.ExternalLoginSignInAsync(loginInfo.LoginProvider, loginInfo.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (!externalLoginResult.Succeeded)
            {
                //TODO: Locked out not work. Need to add some API methods to support lockout data.
                if (externalLoginResult.IsLockedOut)
                {
                    return View("lockedout", WorkContext);
                }

                IdentityResult identityResult;

                var currentUser = WorkContext.CurrentUser;
                if (currentUser.IsRegisteredUser)
                {
                    identityResult = await _signInManager.UserManager.AddLoginAsync(currentUser, loginInfo);
                }
                else
                {
                    user = new User
                    {
                        Email = loginInfo.Principal.FindFirstValue(ClaimTypes.Email),
                        UserName = $"{loginInfo.LoginProvider}--{loginInfo.ProviderKey}",
                        StoreId = WorkContext.CurrentStore.Id,
                    };

                    user.ExternalLogins.Add(new ExternalUserLoginInfo { ProviderKey = loginInfo.ProviderKey, LoginProvider = loginInfo.LoginProvider });

                    var userRegistration = new UserRegistration
                    {
                        FirstName = loginInfo.Principal.FindFirstValue(_firstNameClaims, "unknown"),
                        LastName = loginInfo.Principal.FindFirstValue(ClaimTypes.Surname),
                        UserName = user.UserName,
                        Email = user.Email
                    };
                    user.Contact = userRegistration.ToContact();

                    identityResult = await _signInManager.UserManager.CreateAsync(user);
                    if (identityResult.Succeeded)
                    {
                        await _publisher.Publish(new UserRegisteredEvent(WorkContext, user, userRegistration));
                    }
                }

                if (!identityResult.Succeeded)
                {
                    WorkContext.Form.Errors.AddRange(identityResult.Errors.Select(x => new FormError { Code = x.Code.PascalToKebabCase(), Description = x.Description }));
                    return View("customers/login", WorkContext);
                }
            }

            user = await _signInManager.UserManager.FindByLoginAsync(loginInfo.LoginProvider, loginInfo.ProviderKey);

            if (!externalLoginResult.Succeeded)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
            }
            await _publisher.Publish(new UserLoginEvent(WorkContext, user));

            return StoreFrontRedirect(returnUrl);
        }

        [HttpGet("forgotpassword")]
        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View("customers/forgot_password", WorkContext);
        }

        [HttpPost("forgotpassword")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ForgotPassword(ForgotPassword formModel)
        {
            TryValidateModel(formModel);
            if (!ModelState.IsValid)
            {
                return View("customers/forgot_password", WorkContext);
            }

            var user = await _signInManager.UserManager.FindByEmailAsync(formModel.Email);

            if (user == null)
            {
                user = await _signInManager.UserManager.FindByNameAsync(formModel.Email);
            }

            if (user == null)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.OperationFailed());
                return View("customers/forgot_password", WorkContext);
            }

            var successViewName = "customers/forgot_password";
            NotificationBase resetPasswordNotification = null;

            if (_options.ResetPasswordNotificationGateway.EqualsInvariant("Phone"))
            {
                successViewName = "customers/forgot_password_code";
                var phoneNumber = await _signInManager.UserManager.GetPhoneNumberAsync(user);

                if (string.IsNullOrEmpty(phoneNumber))
                {
                    WorkContext.Form.Errors.Add(SecurityErrorDescriber.OperationFailed());
                    return View("customers/forgot_password", WorkContext);
                }

                var token = await _signInManager.UserManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "ResetPassword");

                resetPasswordNotification = new ResetPasswordSmsNotification(WorkContext.CurrentStore.Id, WorkContext.CurrentLanguage)
                {
                    Token = token,
                    Recipient = phoneNumber,
                };

                //This required for populate hidden fields on the form
                WorkContext.Form = Form.FromObject(new ResetPasswordByCodeModel
                {
                    Email = user.Email
                });
            }
            else // "Email"
            {
                var token = await _signInManager.UserManager.GeneratePasswordResetTokenAsync(user);
                var callbackUrl = Url.Action("ResetPassword", "Account", new { UserId = user.Id, Token = token }, protocol: Request.Scheme, host: WorkContext.CurrentStore.Host);

                resetPasswordNotification = new ResetPasswordEmailNotification(WorkContext.CurrentStore.Id, WorkContext.CurrentLanguage)
                {
                    Url = callbackUrl,
                    Sender = WorkContext.CurrentStore.Email,
                    Recipient = GetUserEmail(user)
                };
            }

            var sendingResult = await SendNotification(resetPasswordNotification);
            if (sendingResult.IsSuccess == true)
            {
                return View(successViewName, WorkContext);
            }

            WorkContext.Form.Errors.Add(new FormError { Description = sendingResult.ErrorMessage });
            return View("customers/forgot_password", WorkContext);
        }

        [HttpGet("forgotlogin")]
        [AllowAnonymous]
        public ActionResult ForgotLogin()
        {
            return View("customers/forgot_login", WorkContext);
        }

        [HttpPost("forgotlogin")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ForgotLogin(ForgotPassword formModel)
        {
            var user = await _signInManager.UserManager.FindByEmailAsync(formModel.Email);
            if (user != null)
            {
                var remindUserNameNotification = new RemindUserNameNotification(WorkContext.CurrentStore.Id, WorkContext.CurrentLanguage)
                {
                    UserName = user.UserName,
                    Sender = WorkContext.CurrentStore.Email,
                    Recipient = GetUserEmail(user)
                };

                var sendingResult = await _platformNotificationApi.SendNotificationAsync(remindUserNameNotification.ToNotificationDto());
                if (sendingResult.IsSuccess != true)
                {
                    WorkContext.Form.Errors.Add(new FormError { Description = sendingResult.ErrorMessage });
                }
            }
            else
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.UserNotFound());
            }
            return View("customers/forgot_login", WorkContext);
        }

        [HttpPost("forgotpasswordbycode")]
        [AllowAnonymous]
        public async Task<ActionResult> ResetPasswordByCode(ResetPasswordByCodeModel formModel)
        {
            TryValidateModel(formModel);
            //Reassign the passed form to the current context to allow user post it again as hidden fields in the form
            WorkContext.Form = Form.FromObject(formModel);

            if (!ModelState.IsValid)
            {
                return View("customers/forgot_password_code", WorkContext);
            }

            if (!_options.ResetPasswordNotificationGateway.EqualsInvariant("Phone"))
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.ResetPasswordIsTurnedOff());
                return View("customers/forgot_password_code", WorkContext);
            }

            var user = await _signInManager.UserManager.FindByEmailAsync(formModel.Email);
            if (user == null)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.OperationFailed());
                return View("customers/forgot_password_code", WorkContext);
            }

            var isValidToken = await _signInManager.UserManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "ResetPassword", formModel.Code);

            if (!isValidToken)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.InvalidToken());
                return View("customers/forgot_password_code", WorkContext);
            }

            var token = await _signInManager.UserManager.GeneratePasswordResetTokenAsync(user);

            WorkContext.Form = Form.FromObject(new ResetPassword
            {
                Token = token,
                Email = user.Email,
                UserName = user.UserName
            });

            return View("customers/reset_password", WorkContext);
        }

        [HttpGet("resetpassword")]
        [AllowAnonymous]
        public async Task<ActionResult> ResetPassword(string token, string userId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.InvalidUrl());
                return View("error", WorkContext);
            }

            var user = await _signInManager.UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.UserNotFound());
                return View("error", WorkContext);
            }

            var isValidToken = await _signInManager.UserManager.VerifyUserTokenAsync(user, _signInManager.UserManager.Options.Tokens.PasswordResetTokenProvider, UserManager<User>.ResetPasswordTokenPurpose, token);

            if (!isValidToken)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.InvalidToken());
                return View("error", WorkContext);
            }

            WorkContext.Form = Form.FromObject(new ResetPassword
            {
                Token = token,
                Email = user.Email,
                UserName = user.UserName
            });
            return View("customers/reset_password", WorkContext);
        }

        [HttpPost("resetpassword")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPassword formModel)
        {
            TryValidateModel(formModel);
            //Need reassign the passed form to the current context to allow for user post it again with initial data such as Token and Email
            WorkContext.Form = Form.FromObject(formModel);

            if (string.IsNullOrEmpty(formModel.Email) && string.IsNullOrEmpty(formModel.UserName))
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.ResetPasswordInvalidData());
            }

            if (formModel.Password != formModel.PasswordConfirmation)
            {
                WorkContext.Form.Errors.Add(SecurityErrorDescriber.PasswordAndConfirmPasswordDoesNotMatch());
            }

            if (!ModelState.IsValid)
            {
                return View("customers/reset_password", WorkContext);
            }

            User user = null;

            if (!string.IsNullOrEmpty(formModel.UserName))
            {
                user = await _signInManager.UserManager.FindByNameAsync(formModel.UserName);
            }

            if (user == null && !string.IsNullOrEmpty(formModel.Email))
            {
                user = await _signInManager.UserManager.FindByEmailAsync(formModel.Email);
            }

            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            var result = await _signInManager.UserManager.ResetPasswordAsync(user, formModel.Token, formModel.Password);

            if (result.Succeeded)
            {
                return View("customers/reset_password_confirmation", WorkContext);
            }

            WorkContext.Form.Errors.AddRange(result.Errors.Select(x => new FormError { Code = x.Code.PascalToKebabCase(), Description = x.Description }));
            return View("customers/reset_password", WorkContext);
        }

        [HttpPost("password")]
        public async Task<ActionResult> ChangePassword(ChangePassword formModel)
        {
            var result = await _signInManager.UserManager.ChangePasswordAsync(WorkContext.CurrentUser, formModel.OldPassword, formModel.NewPassword);

            if (result.Succeeded)
            {
                return StoreFrontRedirect("~/account");
            }
            else
            {
                WorkContext.Form.Errors.AddRange(result.Errors.Select(x => new FormError { Code = x.Code.PascalToKebabCase(), Description = x.Description }));
                return View("customers/account", WorkContext);
            }
        }

        private static string GetUserEmail(User user)
        {
            string email = null;
            if (user != null)
            {
                email = user.Email ?? user.UserName;
                if (user.Contact != null)
                {
                    email = user.Contact?.Email ?? email;
                }
            }
            return email;
        }


        private async Task<SendNotificationResult> SendNotification(NotificationBase notification)
        {
            var result = new SendNotificationResult();

            try
            {
                result = await _platformNotificationApi.SendNotificationAsync(notification.ToNotificationDto());
            }
            catch
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Error occurred while sending notification";
            }

            return result;
        }
    }
}
