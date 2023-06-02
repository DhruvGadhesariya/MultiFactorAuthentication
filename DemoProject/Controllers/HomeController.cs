﻿using DemoProject.Entities.Data;
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

namespace DemoProject.Controllers
{

    public class HomeController : Controller
    {
        #region variables

        private readonly ILogger<HomeController> _logger;

        public readonly DemoDbContext _dbcontext;

        public readonly IDemoRepository _demoreppo;
        #endregion

        #region constructor , dependency inject
        public HomeController(ILogger<HomeController> logger, DemoDbContext dbcontext, IDemoRepository demoRepository)
        {
            _logger = logger;
            _dbcontext = dbcontext;
            _demoreppo = demoRepository;
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
            else if (emailChecked == false)
            {
                TempData["error"] = "Email is Not in format!!";
                return View(model);
            }
            TempData["error"] = "Enter Appropriate Data";
            ViewBag.countries = _dbcontext.Countries.ToList();
            return View(model);
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
                    var userExists = _demoreppo.AddUser(model);
                    TempData["success"] = "OTP has been verified and you have registered successfully.";
                    return RedirectToAction("Index", "Home");
                }
                else if (check == null)
                {
                    TempData["error"] = "OTP is not Correct..";
                    return RedirectToAction("Registration", "Home");
                }
                else
                {
                    if (_demoreppo.IsWithinFiveMinutes(check.CreatedAt) == false)
                    {
                        TempData["error"] = "Otp is expired!!";
                    }
                }
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
                        return View(model);
                    }
                }
                else
                {
                    TempData["error"] = "Enter Valid Email or Password!!";
                }
            }
            else if (emailChecked == false)
            {
                TempData["error"] = "Email is not in format!!";
            }
            TempData["error"] = "Enter Valid Email or Password!!";
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

        public JsonResult GetCity(long countryId)
        {
            return Json(JsonSerializer.Serialize(_demoreppo.GetCityData(countryId)));
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
            ViewBag.Totalpages1 = Math.Ceiling(list / obj.PageSize);
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
                
                ViewBag.Totalpages1 = Math.Ceiling(list / obj.PageSize);
            }
            else
            {
                ViewBag.Totalpages1 = Math.Ceiling(usersData / obj.PageSize);
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
            //List<User> usersData = JsonSerializer.Deserialize<List<User>>(tableData);

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

        public void OrderProduct(OrderParams order)
        {
            var userId = HttpContext.Session.GetInt32("userid");
            bool orderd =  _demoreppo.OrderProducts(order , userId);
            if (orderd)
            {
                TempData["success"] = "Ordered sucessfully";
            }
            else
            {
                TempData["error"] = "Detail is InCorrect!!";
            }
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
    }
}