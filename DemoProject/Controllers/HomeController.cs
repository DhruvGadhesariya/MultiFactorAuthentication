﻿
#region Using Statements
using DemoProject.Entities.Data;
using DemoProject.Entities.Models;
using DemoProject.Entities.ViewModel;
using DemoProject.Models;
using DemoProject.Repository;
using DemoProject.Repository.Interface;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;
using System.Data;
using Azure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System;
using System.IO;
using Microsoft.AspNetCore.Mvc.ModelBinding;

#endregion


namespace DemoProject.Controllers
{

    public class HomeController : Controller
    {
        #region variables

        private readonly ILogger<HomeController> _logger;

        public readonly DemoDbContext _dbcontext;

        public readonly IDemoRepository _demoreppo;

        public readonly IProductRepository _product;
        #endregion

        #region constructor , dependency inject
        public HomeController(ILogger<HomeController> logger, DemoDbContext dbcontext, IDemoRepository demoRepository , IProductRepository product)
        {
            _logger = logger;
            _dbcontext = dbcontext;
            _demoreppo = demoRepository;
            _product = product;
        }

        #endregion

        #region Logout
        [Route("/logout")]
        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.SignOutAsync().Wait();
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }
        #endregion

        #region Registration

        public IActionResult Registration()
        {
            ViewBag.countries = _dbcontext.Countries.ToList();
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Registration(RegistrationModel model)
        {
            var emailChecked = _demoreppo.IsValidEmailAddress(model.Email);

            if (ModelState.IsValid && emailChecked)
            {
                HttpContext.Session.SetString("email", model.Email);
                return RedirectToAction("OtpVerification", "Home", model);
            }
            else if (ModelState.IsValid &&  emailChecked == false)
            {
                TempData["error"] = "Email is Not in format!!";
                return View(model);
            }
            else
            {
                TempData["error"] = "Enter Appropriate Data";
                ViewBag.countries = _dbcontext.Countries.ToList();
                return View(model);
            }      
        }

        #endregion

        #region OtpVerification

        public IActionResult OtpVerification(RegistrationModel model)
        {
            
                long otp = _demoreppo.GenerateRandomOtp();
                SendEmailAsync(model.Email, otp);

                UserOtp existingOtp = _dbcontext.UserOtps.FirstOrDefault(a => a.Email.ToLower() == model.Email.ToLower());
                DateTime currentDateTime = DateTime.Now;
                DateTime expirationDateTime = currentDateTime.AddMinutes(5);

                if (existingOtp == null)
                {
                    _demoreppo.CreateUserOtp(model.Email, otp, currentDateTime, expirationDateTime);
                }
                else
                {
                    _demoreppo.UpdateUserOtp(existingOtp, otp, currentDateTime, expirationDateTime);
                }
            model.Otp = null; // Set the Otp property to null

            return View(model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult OtpVerification(UserVerifyViewmodel model)
        {
            if (model.Otp != 0)
            {
                var email = HttpContext.Session.GetString("email");
                if (email == null)
                {
                    return RedirectToAction("Index", "Home");
                }

                var check = _dbcontext.UserOtps.Where(otp => otp.Email.ToLower() == email.ToLower() && otp.Otp == model.Otp).FirstOrDefault();
                if (check != null && email != null)
                {
                    if (_demoreppo.IsWithinFiveMinutes(check.CreatedAt) == false)
                    {
                        TempData["error"] = "Otp is expired!!";
                        return RedirectToAction("Registration", "Home");
                    }
                    var userExists = _demoreppo.AddUser(model);
                    if (userExists)
                    {
                        TempData["success"] = "OTP has been verified and you have registered successfully.";
                        return RedirectToAction("Index", "Home");
                    }
                    else
                    {
                        TempData["success"] = "You have already registered.";
                        return RedirectToAction("Index", "Home");
                    }
                    
                }
                else if (check == null)
                {
                    TempData["error"] = "OTP is not Correct..";
                    return RedirectToAction("Registration", "Home");
                }
            }
            else
            {
                TempData["error"] = "Enter Appropriate OTP";
                return View();
            }
            TempData["error"] = "Enter Appropriate OTP";
            return View(model);
        }

        #endregion

        #region SendOTP 
        [Route("Home/SendEmailAsync")]
        public Task SendEmailAsync(string email, long otp)
        {
            var subject = "Otp Verification for Registration";

            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential("19it.dhruvgadhesariya@adit.ac.in", "Adit@884369")
            };

            return client.SendMailAsync(new MailMessage(from: "19it.dhruvgadhesariya@adit.ac.in",
                                                        to: email,
                                                        subject,
                                                        "Your Otp is : " + otp));
        }


        
        #endregion

        #region Login

        public IActionResult Index()
        {
            HttpContext.SignOutAsync().Wait();
            HttpContext.Session.Clear();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(LoginViewModel model)
        {

            var emailChecked = _demoreppo.IsValidEmailAddress(model.Email);

            if (ModelState.IsValid && emailChecked == true)
            {
                var check = _dbcontext.Users.FirstOrDefault(user => user.Email.ToLower() == model.Email.ToLower());

                if (check != null)
                {
                    bool verify = BCrypt.Net.BCrypt.Verify(model.Password, check.Password);
                    if (verify)
                    {
                        var claims = new List<Claim>
                        {
                        new Claim("Email", model.Email),
                        };
                        var identity = new ClaimsIdentity(claims, "AuthCookie");
                        var Principle = new ClaimsPrincipal(identity);
                        HttpContext.User = Principle;

                        var abc = HttpContext.SignInAsync(Principle);

                        HttpContext.Session.SetInt32("userid", (int)check.UserId);
                        TempData["success"] = "Login Successfully.";
                        return RedirectToAction("HomePage", "Home");
                    }
                    else
                    {
                        TempData["error"] = "Enter Valid Email or Password!!";
                        return View();
                    }
                }
                else
                {
                    TempData["error"] = "Enter Valid Email or Password!!";
                }
            }
            else if (ModelState.IsValid && emailChecked == false)
            {
                TempData["error"] = "Email is not in format!!";
                return View();
            }
            else
            {
                TempData["error"] = "Enter Valid Email or Password!!";
                return View();
            }
           
            return View(model);
        }

        #endregion

        #region HomePage

        [Authorize]
        public IActionResult HomePage()
        {
            var list = _dbcontext.Users.Where(a => a.DeletedAt == null).ToList();
            ViewBag.countries = _dbcontext.Countries.ToList();
            ViewBag.Totalpages1 = Math.Ceiling(_dbcontext.Users.Where(a => a.DeletedAt == null).ToList().Count() / 5.0);
            ViewBag.currentPage = 1;
            ViewBag.Finder = "Fname";
            ViewBag.Sort = "up";
            ViewBag.usersList = list;
            ViewBag.pagesize = 5;
            ViewBag.UserId = HttpContext.Session.GetInt32("userid");
            ViewBag.Products = _dbcontext.Products.ToList();
            return View();
        }
        public JsonResult GetCityForRegistration(long countryId)
        {
            return Json(JsonSerializer.Serialize(_demoreppo.GetCityForRegistration(countryId)));
        }
        public JsonResult GetCity(long countryId , long ProductId)
        {
            return Json(JsonSerializer.Serialize(_demoreppo.GetCityData(countryId , ProductId)));
        }

        [HttpPost]
        public void AddUserByMe(User user)
        {
            var userEmail = _dbcontext.Users.Where(a => a.Email == user.Email && a.DeletedAt == null).Select(a => a.Email).FirstOrDefault();
            var emailChecked = _demoreppo.IsValidEmailAddress(user.Email);

            if (userEmail == null && emailChecked == true)
            {
                _demoreppo.addUserByMe(user);
                TempData["success"] = "User has been Added sucessfully";
            }
            else if (emailChecked == false)
            {
                TempData["error"] = "Email is InCorrect!!";
            }
            else
            {
                TempData["error"] = "User Already Exists You Can Edit Details";
            }
        }

        public string GetDataOfUser(long userId)
        {
            var data = JsonSerializer.Serialize(_demoreppo.GetUserDataForAdmin(userId));
            return data;
        }

        [HttpPost]
        public void EditUserByMe(User user)
        {
            var emailChecked = _demoreppo.IsValidEmailAddress(user.Email);

            if (emailChecked == true)
            {
                _demoreppo.editUserByAdmin(user);
                TempData["success"] = "User has been Updated sucessfully";
            }
            else
            {
                TempData["error"] = "Email is not in format!!";
            }
        }
        [HttpPost]
        public void RemoveByAdmin(long Id)
        {
            _demoreppo.RemoveByAdmin(Id);
            TempData["success"] = "You have Removed successfully!!";
        }

        [HttpPost]
        public ActionResult Search(UserSearchParams obj)
        {
            obj.SearchFname = string.IsNullOrEmpty(obj.SearchFname) ? "" : obj.SearchFname;
            obj.SearchLname = string.IsNullOrEmpty(obj.SearchLname) ? "" : obj.SearchLname;
            obj.SearchEmail = string.IsNullOrEmpty(obj.SearchEmail) ? "" : obj.SearchEmail;

            List<User> usersData = _demoreppo.FilterUsers(obj);
            ViewBag.usersList = usersData;

            var list = _dbcontext.Users.Count(a => a.DeletedAt == null);
            ViewBag.Totalpages1 = Math.Ceiling(list / (double)obj.PageSize);
            ViewBag.currentPage = obj.Pg;
            ViewBag.Finder = obj.Finder;
            ViewBag.Sort = obj.Sort;
            ViewBag.pagesize = obj.PageSize;
            ViewBag.countries = _dbcontext.Countries.ToList();
            return PartialView("_UsersCRUD", usersData);
        }

        [HttpPost]
        public ActionResult Pagination(UserSearchParams obj)
        {
            obj.SearchFname = string.IsNullOrEmpty(obj.SearchFname) ? "" : obj.SearchFname;
            obj.SearchLname = string.IsNullOrEmpty(obj.SearchLname) ? "" : obj.SearchLname;
            obj.SearchEmail = string.IsNullOrEmpty(obj.SearchEmail) ? "" : obj.SearchEmail;

            var usersData = _demoreppo.FilterUsersForDownload(obj).Count();
            ViewBag.usersList = usersData;
            var list = _dbcontext.Users.Count(a => a.DeletedAt == null);
            if (string.IsNullOrEmpty(obj.SearchEmail) && string.IsNullOrEmpty(obj.SearchLname) && string.IsNullOrEmpty(obj.SearchFname))
            { 
                ViewBag.Totalpages1 = Math.Ceiling(list / (double)obj.PageSize);
            }
            else
            {
                ViewBag.Totalpages1 = Math.Ceiling(usersData / (double)obj.PageSize);
            }
            ViewBag.currentPage = obj.Pg;
            ViewBag.Finder = obj.Finder;
            ViewBag.Sort = obj.Sort;
            ViewBag.pagesize = obj.PageSize;
            return PartialView("_Pagination");
        }

        [HttpPost]
        public void DownloadData(string format, UserSearchParams obj)
        {
            var userId = HttpContext.Session.GetInt32("userid");
            if (format == "pdf")
            {

                _demoreppo.DownLoadPdf(userId, obj);
            }
            else 
            {
                _demoreppo.DownLoadExcel(userId, obj);
            }
        }
        #endregion

        #region Orders
        public IActionResult MailBody()
        {
            return View();
        }

        [HttpPost]
        public IActionResult MailBody(long OrderId , string[] EmailList)
        {
            // Get the order details using the repository
            var model = _demoreppo.GetOrderDetailForPreview(OrderId , EmailList);
            return View(model);
        }

        public void SendEmailForOrder(OrderParams order, string email)
        {
            // Get the user ID from the session
            var userId = HttpContext.Session.GetInt32("userid");

            // Set the subject for the email
            var subject = "Order Placed..";

            // Get the order details using the repository
            var model = _demoreppo.GetOrderDetail(order, (long)userId);

            // Render the view to a string
            var body = RenderViewToString(ControllerContext, "MailBody", model);

            // Create a new mail message
            var message = new MailMessage
            {
                From = new MailAddress("dhruv.gadhesariya@internal.mail"),
                To = { new MailAddress(email) },
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            // Set the attachment file path
            string attachmentFilePath1 = "C:\\Users\\pca140\\source\\repos\\DemoProject\\DemoProject\\wwwroot\\Downloads\\filtered_19-.pdf";

            // Create an attachment and add it to the message
            Attachment attachment1 = new Attachment(attachmentFilePath1);
            message.Attachments.Add(attachment1);

            // Get the current order date and add it to the message headers
            DateTime orderDate = DateTime.Now;
            message.Headers.Add("Order-Date", orderDate.ToString("yyyy-MM-dd"));

            // Create an alternate view for the HTML body
            AlternateView htmlView = AlternateView.CreateAlternateViewFromString(body, null, "text/html");

            // Add linked resources images to the HTML view
            LinkedResource imageResource = new LinkedResource("C:\\Users\\pca140\\source\\repos\\DemoProject\\DemoProject\\wwwroot\\Assets\\multifactor-authentificaton.png", "image/png");
            imageResource.ContentId = "myImage";
            htmlView.LinkedResources.Add(imageResource);

            LinkedResource imageResource2 = new LinkedResource("C:\\Users\\pca140\\source\\repos\\DemoProject\\DemoProject\\wwwroot\\Assets\\download.jpg", "image/jpg");
            imageResource2.ContentId = "myImage1";
            htmlView.LinkedResources.Add(imageResource2);

            // Add the HTML view to the message
            message.AlternateViews.Add(htmlView);

            try
            {
                // Create a new SMTP client and send the message
                using (var client = new SmtpClient("172.16.10.7", 25))
                {
                    client.EnableSsl = false;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("dhruv.gadhesariya@internal.mail", "tatva123");
                    client.Send(message);
                }
            }
            catch (SmtpException ex)
            {
                TempData["error"] = "error in sending the emails" + ex.Message;
            }
        }

        public string RenderViewToString(ControllerContext context, string viewName, object model)
        {
            // Get the view engine from the request services
            var viewEngine = context.HttpContext.RequestServices.GetService(typeof(ICompositeViewEngine)) as ICompositeViewEngine;

            // Find the view based on the view name
            var viewResult = viewEngine.FindView(context, viewName, false);

            if (viewResult.View == null)
            {
                throw new ArgumentNullException($"{viewName} does not match any view");
            }

            // Create a view data dictionary with the model
            var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), context.ModelState)
            {
                Model = model
            };

            using (var sw = new StringWriter())
            {
                // Create a view context and render the view to the string writer
                var viewContext = new ViewContext(
                    context,
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(context.HttpContext, context.HttpContext.RequestServices.GetRequiredService<ITempDataProvider>()),
                    sw,
                    new HtmlHelperOptions()
                );

                viewResult.View.RenderAsync(viewContext).GetAwaiter().GetResult();

                // Return the rendered view as a string
                return sw.ToString();
            }
        }

        public string OrderProduct(OrderParams order)
        {
            var userId = HttpContext.Session.GetInt32("userid");
            string orderd =  _demoreppo.OrderProducts(order , userId);
            if (orderd == "true")
            {
                SendEmailForOrder(order, "dhruv.gadhesariya@internal.mail");
                TempData["success"] = "Order Placed Successfully!!";
                return "true";
            }
            else
            {
                if (orderd == "falseTime")
                {
                    return "falseTime";
                }
                else if(orderd == "notAvailable")
                {
                    return "notAvailable";
                }
            }
            return "";
        }

        public string GetAvailableCountryForProduct(long ProductId)
        {
            return JsonSerializer.Serialize(_demoreppo.GetAvailableCountry(ProductId));
        }
        #endregion

        #region Privacy and error
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        #endregion

        #region SendMails 
        public IActionResult SendInvite(long orderId)
        {
            ViewBag.OrderId = orderId;
            return View();
        }

        [HttpPost]
        public ActionResult SendInvite(long orderId, string[] EmailList)
        {
            // Check if the email addresses are valid
            var checkEmails = _product.AreValidEmailAddresses(EmailList);

            if (checkEmails)
            {
                // Get order data
                var model = _product.GetOrderData(orderId);
                var subject = "Order Placed..";

                // Render the view to a string
                var body = RenderViewToString(ControllerContext, "MailBody", model);

                // Create a new mail message
                var message = new MailMessage
                {
                    From = new MailAddress("dhruv.gadhesariya@internal.mail"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                // Add To mails
                foreach (var email in EmailList)
                {
                    message.To.Add(email);
                }

                // Attach file
                string attachmentFilePath1 = "C:\\Users\\pca140\\source\\repos\\DemoProject\\DemoProject\\wwwroot\\Downloads\\filtered_19-.pdf";
                Attachment attachment1 = new Attachment(attachmentFilePath1);
                message.Attachments.Add(attachment1);

                // Add custom header
                DateTime orderDate = DateTime.Now;
                message.Headers.Add("Order-Date", orderDate.ToString("yyyy-MM-dd"));

                // Create alternate view for HTML body
                AlternateView htmlView = AlternateView.CreateAlternateViewFromString(body, null, "text/html");

                // Add linked resources images
                LinkedResource imageResource = new LinkedResource("C:\\Users\\pca140\\source\\repos\\DemoProject\\DemoProject\\wwwroot\\Assets\\multifactor-authentificaton.png", "image/png");
                imageResource.ContentId = "myImage";
                htmlView.LinkedResources.Add(imageResource);

                LinkedResource imageResource2 = new LinkedResource("C:\\Users\\pca140\\source\\repos\\DemoProject\\DemoProject\\wwwroot\\Assets\\download.jpg", "image/jpg");
                imageResource2.ContentId = "myImage1";
                htmlView.LinkedResources.Add(imageResource2);

                // Add alternate view to the message
                message.AlternateViews.Add(htmlView);

                try
                {
                    // Send the email using SmtpClient
                    using (var client = new SmtpClient("172.16.10.7", 25))
                    {
                        client.EnableSsl = false;
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential("dhruv.gadhesariya@internal.mail", "tatva123");

                        client.Send(message);
                    }
                }
                catch (SmtpException ex)
                {
                    TempData["error"] = "error in sending the emails" + ex.Message;
                }
            }
            else
            {
                TempData["error"] = "One of the email is not correct!!";
            }
            TempData["success"] = "Mail sent successfully!!";
            return View();
        }

        #endregion
    }
}