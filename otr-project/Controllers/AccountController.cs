﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using System.IO;
using System.Data;
using otr_project.Models;
using Mvc.Mailer;
using otr_project.Mailers;
using System.Data.Entity;
using Facebook;
using Facebook.Web;
using otr_project.ViewModels;
using log4net;

namespace otr_project.Controllers
{
    [LoggingFilter]
    public class AccountController : Controller
    {
        public IFormsAuthenticationService FormsService { get; set; }
        public IMembershipService MembershipService { get; set; }
        public MarketPlaceEntities market = new MarketPlaceEntities();
        private IUserMailer _userMailer = new UserMailer();
        ErrorMessageViewModel ErrorMessage = new ErrorMessageViewModel();
        private static readonly ILog log = LogManager.GetLogger("otr_project.MvcApplication.Controllers");

        public IUserMailer UserMailer
        {
            get { return _userMailer; }
            set { _userMailer = value; }
        }

        protected override void Initialize(RequestContext requestContext)
        {
            if (FormsService == null) { FormsService = new FormsAuthenticationService(); }
            if (MembershipService == null) { MembershipService = new AccountMembershipService(); }

            base.Initialize(requestContext);
        }

        // **************************************
        // URL: /Account/LogOn
        // **************************************

        public ActionResult LogOn()
        {
            if (FacebookWebContext.Current.IsAuthenticated() && FacebookWebContext.Current.IsAuthorized())
            {
                FacebookClient fbClient = new FacebookClient(FacebookWebContext.Current.AccessToken);
                dynamic me = fbClient.Get("me");
                string facebookId = (string)me.id;

                var fbUser = market.FacebookUsers.SingleOrDefault(f => f.Id == facebookId);

                if (fbUser == null)
                {
                    var location = me.location;
                    if (location == null)
                    {
                        location = me.hometown;
                        if (location == null)
                        {
                            return View();
                        }
                    }
                    string[] address = ((string)location.name).Split(',');
                    string city = address[0].Trim();
                    string prov = address[1].Trim();

                    //User doesnt exist. Let us create one
                    //Creating a regular user profile
                    market.Users.Add(new UserModel
                    {
                        Email = (string)me.email,
                        FirstName = (string)me.first_name,
                        LastName = (string)me.last_name,
                        City = city,
                        RegionId = market.Regions.SingleOrDefault(r=> r.Name.Contains(prov)).Id,
                        CountryId = 1,
                        isFacebookUser = true,
                        FacebookUserId = facebookId
                    });

                    //market.SaveChanges();

                    //Now creating the facebook user profile. We can wire this to the regular user by using a foreign key
                    market.FacebookUsers.Add(new FacebookUser
                    {
                        Id = facebookId,
                        UserModelEmail = (string)me.email,
                        AccessToken = FacebookWebContext.Current.AccessToken,
                        Expires = FacebookWebContext.Current.Session.Expires
                    });
                    log.Info("Account - Creating new Facebook user (" + (string)me.first_name + " " + (string)me.last_name + ")");
                    market.SaveChanges();
                }

                //User is either created or he/she exists. Let us sign them in using email as the username.
                FormsAuthentication.SetAuthCookie((string)me.email, false);
                Session["USER_F_NAME"] = (string)me.first_name;
                log.Info("Account - User logged in (" + (string)me.email + ")");
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        public ActionResult LogOnAjaxFacebook()
        {
            var result = new LogOnAjaxViewModel();
            if (FacebookWebContext.Current.IsAuthenticated() && FacebookWebContext.Current.IsAuthorized())
            {
                FacebookClient fbClient = new FacebookClient(FacebookWebContext.Current.AccessToken);
                dynamic me = fbClient.Get("me");
                string facebookId = (string)me.id;

                var fbUser = market.FacebookUsers.SingleOrDefault(f => f.Id == facebookId);

                if (fbUser == null)
                {
                    var location = me.location;
                    if (location == null)
                    {
                        location = me.hometown;
                        if (location == null)
                        {
                            result.Error = true;
                            result.Message = "Facebook authentication failed.";
                            return Json(result);
                        }
                    }
                    string[] address = ((string)location.name).Split(',');
                    string city = address[0].Trim();
                    string prov = address[1].Trim();

                    //User doesnt exist. Let us create one
                    //Creating a regular user profile
                    market.Users.Add(new UserModel
                    {
                        Email = (string)me.email,
                        FirstName = (string)me.first_name,
                        LastName = (string)me.last_name,
                        City = city,
                        RegionId = market.Regions.SingleOrDefault(r => r.Name.Contains(prov)).Id,
                        CountryId = 1,
                        isFacebookUser = true,
                        FacebookUserId = facebookId
                    });

                    //market.SaveChanges();

                    //Now creating the facebook user profile. We can wire this to the regular user by using a foreign key
                    market.FacebookUsers.Add(new FacebookUser
                    {
                        Id = facebookId,
                        UserModelEmail = (string)me.email,
                        AccessToken = FacebookWebContext.Current.AccessToken,
                        Expires = FacebookWebContext.Current.Session.Expires
                    });
                    log.Info("Account - Creating new Facebook user (" + (string)me.first_name + " " + (string)me.last_name + ")");
                    market.SaveChanges();
                }

                //User is either created or he/she exists. Let us sign them in using email as the username.
                FormsAuthentication.SetAuthCookie((string)me.email, false);
                Session["USER_F_NAME"] = (string)me.first_name;
                log.Info("Account - User logged in (" + (string)me.email + ")");
                result.Error = false;
                return Json(result);
            }
            result.Error = true;
            result.Message = "Facebook authentication failed.";
            return Json(result);
        }

        [HttpPost]
        public ActionResult LogOn(LogOnModel model, string returnUrl)
        {
            if (ModelState.IsValid)
            {
                if (MembershipService.ValidateUser(model.Email.ToLower(), model.Password))
                {
                    FormsService.SignIn(model.Email.ToLower(), model.RememberMe);
                    Session["USER_F_NAME"] = market.Users.Find(model.Email.ToLower()).FirstName;
                    log.Info("Account - User logged in (" + model.Email.ToLower() + ")");
                    if (Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }
                    else
                    {
                        return RedirectToAction("Index", "Home");
                    }
                }
                else
                {
                    log.Error("Account - Incorrect login attempt (" + model.Email.ToLower() + "/" + model.Password + ")");
                    ModelState.AddModelError("", "The email address and/or password provided is incorrect.");
                    return View(model);
                }
            }
            log.Error("Account - LogOnModel Error (" + model.Email + "/" + model.Password + "/" + model.RememberMe + ")");
            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpPost]
        public ActionResult LogOnAjax(LogOnModel model)
        {
            var result = new LogOnAjaxViewModel();
            if (ModelState.IsValid)
            {
                if (MembershipService.ValidateUser(model.Email.ToLower(), model.Password))
                {
                    FormsService.SignIn(model.Email.ToLower(), model.RememberMe);
                    Session["USER_F_NAME"] = market.Users.Find(model.Email.ToLower()).FirstName;
                    log.Info("Account - User logged in (" + model.Email.ToLower() + ")");
                    result.Error = false;
                    return Json(result);
                }
                else
                {
                    log.Error("Account - Incorrect login attempt (" + model.Email.ToLower() + "/" + model.Password + ")");
                    ModelState.AddModelError("", "The email address and/or password provided is incorrect.");
                    result.Error = true;
                    result.Message = "The email address and/or password provided is incorrect.";
                    return Json(result);
                }
            }
            log.Error("Account - LogOnModel Error (" + model.Email + "/" + model.Password + "/" + model.RememberMe + ")");
            result.Error = true;
            result.Message = "Username and password are required fields.";
            return Json(result);
        }

        // **************************************
        // URL: /Account/LogOff
        // **************************************

        public ActionResult LogOff()
        {
            var cart = RentalCart.GetCart(this.HttpContext);
            cart.EmptyCart();
            
            FormsService.SignOut();
            FacebookWebContext.Current.DeleteAuthCookie();

			Session["RentalCartItems"] = 0;
            return RedirectToAction("Index", "Home");
        }

        // **************************************
        // URL: /Account/Register
        // **************************************

        public ActionResult Register()
        {
            if (FacebookWebContext.Current.IsAuthenticated() && FacebookWebContext.Current.IsAuthorized())
            {
                return RedirectToAction("LogOn");
            }
            ViewBag.PasswordLength = MembershipService.MinPasswordLength;
            ViewBag.RegionId = new SelectList(market.Regions, "Id", "Name");
            return View();
        }

        [HttpPost]
        public ActionResult Register(RegisterModel model)
        {
            ViewBag.RegionId = new SelectList(market.Regions, "Id", "Name");

            if (ModelState.IsValid)
            {
                UserModel user = new UserModel();
                // Attempt to register the user
                MembershipCreateStatus createStatus = MembershipService.CreateUser(model.Password, model.Email.ToLower());

                if (createStatus == MembershipCreateStatus.Success)
                {
                    user.ActivationId = System.Guid.NewGuid().ToString();
                    MembershipUser RegisteredUser = Membership.GetUser(model.Email.ToLower());
                    RegisteredUser.IsApproved = false;
                    Membership.UpdateUser(RegisteredUser);
                    user.Email = model.Email.ToLower();
                    user.FirstName = model.FirstName.Substring(0, 1).ToUpper() + model.FirstName.Substring(1);
                    user.LastName = model.LastName.Substring(0, 1).ToUpper() + model.LastName.Substring(1);
                    user.City = model.City;
                    user.RegionId = model.RegionId;
                    user.CountryId = 1;
                    market.Badges.SingleOrDefault(b => b.Name == "Individual").Users.Add(user);
                    market.Users.Add(user);
                    market.SaveChanges();

                    //Use MvcMailer to send welcome email to newly registered user.
                    UserMailer.Welcome(newUser: user).Send();
                    Session["REGISTERED_USER"] = true;
                    log.Info("Account - New user registered (" + model.Email.ToLower() + ")");
                    return View();
                }
                else
                {
                    log.Error("Account - Error registering new user. " + createStatus.ToString());
                    ModelState.AddModelError("", AccountValidation.ErrorCodeToString(createStatus));
                }
            }

            log.Error("Account - RegisterModel Error.");
            // If we got this far, something failed, redisplay form
            ViewBag.PasswordLength = MembershipService.MinPasswordLength;
            return View(model);
        }
        
        public ActionResult ActivateAccount(string id)
        {
            var user = market.Users.SingleOrDefault(u => u.ActivationId == id);
            if (user != null)
            {
                MembershipUser RegisteredUser = Membership.GetUser(user.Email);

                if (RegisteredUser.IsApproved == false)
                {
                    RegisteredUser.IsApproved = true;
                    Membership.UpdateUser(RegisteredUser);
                    FormsService.SignIn(user.Email, false /* createPersistentCookie */);
                    Session["USER_F_NAME"] = user.FirstName;
                    log.Info("Account - User account activated (" + user.Email + ")");
                }
            }
            else
            {
                log.Error("Account - Invalid activation ID (" + id + ")");
            }
            return RedirectToAction("Index", "Home");
        }


        // **************************************
        // URL: /Account/ChangePassword
        // **************************************

        [Authorize]
        public ActionResult ChangePassword()
        {
            ViewBag.PasswordLength = MembershipService.MinPasswordLength;
            return View();
        }

        [Authorize]
        [HttpPost]
        public ActionResult ChangePassword(ChangePasswordModel model)
        {
            ViewBag.PasswordLength = MembershipService.MinPasswordLength;
            if (ModelState.IsValid)
            {
                if (MembershipService.ChangePassword(User.Identity.Name, model.OldPassword, model.NewPassword))
                {
                    log.Info("Account - User changed password (" + User.Identity.Name + ")");
                    return RedirectToAction("ChangePasswordSuccess");
                }
                else
                {
                    log.Error("Account - User password change failed (" + User.Identity.Name + ")");
                    ModelState.AddModelError("", "The current password is incorrect or the new password is invalid.");
                    return View(model);
                }
            }
            log.Error("Account - ChangePasswordModel Error.");
            // If we got this far, something failed, redisplay form
            return View(model);
        }

        // **************************************
        // URL: /Account/ChangePasswordSuccess
        // **************************************

        public ActionResult ChangePasswordSuccess()
        {
            return View();
        }
    }
}
