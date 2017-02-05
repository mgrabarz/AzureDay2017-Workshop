﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using Contoso.Apps.SportsLeague.Data.Models;
using Contoso.Apps.SportsLeague.Web.ViewModels;
using Contoso.Apps.SportsLeague.Data.Logic;
using AutoMapper;
using Contoso.Apps.Common;
using Contoso.Apps.SportsLeague.Web.Helpers;
using System.Threading.Tasks;

namespace Contoso.Apps.SportsLeague.Web.Controllers
{
    public class CheckoutController : Controller
    {
        private ProductContext db = new ProductContext();
        private string cartId = new Helpers.CartHelpers().GetCartId();

        // GET: Checkout
        public ActionResult Index()
        {
            
            var vm = new CheckoutViewModel();
            using (ShoppingCartActions usersShoppingCart = new ShoppingCartActions(cartId))
            {
                var shoppingCartItems = usersShoppingCart.GetCartItems();
                var cartItemsVM = Mapper.Map<List<CartItemViewModel>>(shoppingCartItems);
                vm.CartItems = cartItemsVM;
                vm.OrderTotal = usersShoppingCart.GetTotal();
                vm.ItemsTotal = usersShoppingCart.GetCount();
            }

            // To make filling out the form faster for demo purposes, pre-fill the values:
            vm.Order = new OrderViewModel
            {
                // Important! Keep this property here!
                Total = vm.OrderTotal,
                // Prefill properties for convenience:
                FirstName = "Bob",
                LastName = "Loblaw",
                Address = "1313 Mockingbird Lane",
                City = "Virginia Beach",
                State = "VA",
                PostalCode = "23456",
                Country = "United States",
                Email = "bobloblaw@contososportsleague.com",
                NameOnCard = "Bob Loblaw",
                CreditCardType = "Visa",
                CreditCardNumber = "4111111111111111",
                ExpirationDate = "12/20",
                CCV = "987",
                SMSOptIn = true
            };

            return View(vm);
        }

        // POST: Checkout/Start
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Review(CheckoutViewModel data)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    NVPAPICaller gatewayCaller = new NVPAPICaller();

                    string token = "";
                    NVPCodec decoder = new NVPCodec();

                    // Call the gateway payment authorization API:
                    bool ret = gatewayCaller.DoCheckoutAuth(data.Order, ref token, ref decoder);

                    // If authorizaton is successful:
                    if (ret)
                    {
                        // Hydrate a new Order model from our OrderViewModel.
                        var myOrder = Mapper.Map<Data.Models.Order>(data.Order);
                        // Timestamp with a UTC date.
                        myOrder.OrderDate = DateTime.UtcNow;

                        // Get DB context.
                        ProductContext _db = new ProductContext();

                        // Add order to DB.
                        _db.Orders.Add(myOrder);
                        _db.SaveChanges();

                        // Get the shopping cart items and process them.
                        using (ShoppingCartActions usersShoppingCart = new ShoppingCartActions(cartId))
                        {
                            List<CartItem> myOrderList = usersShoppingCart.GetCartItems();

                            // Add OrderDetail information to the DB for each product purchased.
                            for (int i = 0; i < myOrderList.Count; i++)
                            {
                                // Create a new OrderDetail object.
                                var myOrderDetail = new OrderDetail();
                                myOrderDetail.OrderId = myOrder.OrderId;
                                myOrderDetail.ProductId = myOrderList[i].ProductId;
                                myOrderDetail.Quantity = myOrderList[i].Quantity;
                                myOrderDetail.UnitPrice = myOrderList[i].Product.UnitPrice;

                                // Add OrderDetail to DB.
                                _db.OrderDetails.Add(myOrderDetail);
                                _db.SaveChanges();
                            }

                            // Set OrderId.
                            Session["currentOrderId"] = myOrder.OrderId;
                            Session["Token"] = token;

                            // Report successful event to Application Insights.
                            var eventProperties = new Dictionary<string, string>();
                            eventProperties.Add("CustomerEmail", data.Order.Email);
                            eventProperties.Add("NumberOfItems", myOrderList.Count.ToString());
                            eventProperties.Add("OrderTotal", data.Order.Total.ToString("C2"));
                            eventProperties.Add("OrderId", myOrder.OrderId.ToString());
                            TelemetryHelper.TrackEvent("SuccessfulPaymentAuth", eventProperties);

                            data.Order.OrderId = myOrder.OrderId;
                            if (data.Order.CreditCardNumber.Length > 4)
                            {
                                // Only show the last 4 digits of the credit card number:
                                data.Order.CreditCardNumber = "xxxxxxxxxxx" + data.Order.CreditCardNumber.Substring(data.Order.CreditCardNumber.Length - 4);
                            }
                        }
                    }
                    else
                    {
                        var error = gatewayCaller.PopulateGatewayErrorModel(decoder);

                        // Report failed event to Application Insights.
                        Exception ex = new Exception(error.ToString());
                        ex.Source = "Contoso.Apps.SportsLeague.Web.CheckoutController.cs";
                        TelemetryHelper.TrackException(ex);

                        // Redirect to the checkout error view:
                        return RedirectToAction("Error", error);
                    }
                }
                catch (WebException wex)
                {
                    ExceptionUtility.LogException(wex, "CheckoutController.cs Complete Action");

                    var error = new CheckoutErrorViewModel
                    {
                        ErrorCode = wex.Message
                    };

                    if (wex.Response != null && wex.Response.GetType() == typeof(HttpWebResponse))
                    {
                        // Extract the response body from the WebException's HttpWebResponse:
                        error.LongMessage = ((HttpWebResponse)wex.Response).StatusDescription;
                    }

                    // Redirect to the checkout error view:
                    return RedirectToAction("Error", error);
                }
                catch (Exception ex)
                {
                    ExceptionUtility.LogException(ex, "CheckoutController.cs Review Action");

                    var error = new CheckoutErrorViewModel
                    {
                        ErrorCode = ex.Message
                    };

                    // Redirect to the checkout error view:
                    return RedirectToAction("Error", error);
                }
            }

            return View(data);
        }

        // POST: Checkout/Complete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Complete(OrderViewModel order)
        {
            try
            {
                // TODO: Complete the payment processing via the gateway and update the order...
                NVPAPICaller gatewayCaller = new NVPAPICaller();

                string token = "";
                string finalPaymentAmount = "";
                NVPCodec decoder = new NVPCodec();

                token = Session["token"].ToString();
                //PayerID = Session["payerId"].ToString();
                //finalPaymentAmount = Session["payment_amt"].ToString();
                finalPaymentAmount = order.Total.ToString("C2");

                bool ret = gatewayCaller.DoCheckoutPayment(finalPaymentAmount, token, ref decoder);
                if (ret)
                {
                    // Retrieve PayPal confirmation value.
                    string PaymentConfirmation = decoder[NVPProperties.Properties.TRANSACTIONID].ToString();
                    order.PaymentTransactionId = PaymentConfirmation;


                    ProductContext _db = new ProductContext();
                    // Get the current order id.
                    int currentOrderId = -1;
                    if (Session["currentOrderId"] != null && Session["currentOrderId"].ToString() != string.Empty)
                    {
                        currentOrderId = Convert.ToInt32(Session["currentOrderID"]);
                    }
                    Order myCurrentOrder;
                    if (currentOrderId >= 0)
                    {
                        // Get the order based on order id.
                        myCurrentOrder = _db.Orders.Single(o => o.OrderId == currentOrderId);
                        // Update the order to reflect payment has been completed.
                        myCurrentOrder.PaymentTransactionId = PaymentConfirmation;
                        // Save to DB.
                        _db.SaveChanges();

                        // Queue up a receipt generation request, asynchronously.
                        await new AzureQueueHelper().QueueReceiptRequest(currentOrderId);

                        // Report successful event to Application Insights.
                        var eventProperties = new Dictionary<string, string>();
                        eventProperties.Add("CustomerEmail", order.Email);
                        eventProperties.Add("OrderTotal", finalPaymentAmount);
                        eventProperties.Add("PaymentTransactionId", PaymentConfirmation);
                        TelemetryHelper.TrackEvent("OrderCompleted", eventProperties);
                    }

                    // Clear shopping cart.
                    using (ShoppingCartActions usersShoppingCart =
                        new ShoppingCartActions(cartId))
                    {
                        usersShoppingCart.EmptyCart();
                    }

                    // Clear order id.
                    Session["currentOrderId"] = string.Empty;
                }
                else
                {
                    var error = gatewayCaller.PopulateGatewayErrorModel(decoder);

                    // Report failed event to Application Insights.
                    Exception ex = new Exception(error.ToString());
                    ex.Source = "Contoso.Apps.SportsLeague.Web.CheckoutController.cs";
                    TelemetryHelper.TrackException(ex);

                    // Redirect to the checkout error view:
                    return RedirectToAction("Error", error);
                }
            }
            catch (WebException wex)
            {
                ExceptionUtility.LogException(wex, "CheckoutController.cs Complete Action");

                var error = new CheckoutErrorViewModel
                {
                    ErrorCode = wex.Message
                };

                if (wex.Response != null && wex.Response.GetType() == typeof(HttpWebResponse))
                {
                    // Extract the response body from the WebException's HttpWebResponse:
                    error.LongMessage = ((HttpWebResponse)wex.Response).StatusDescription;
                }

                // Redirect to the checkout error view:
                return RedirectToAction("Error", error);
            }
            catch (Exception ex)
            {
                ExceptionUtility.LogException(ex, "CheckoutController.cs Complete Action");

                var error = new CheckoutErrorViewModel
                {
                    ErrorCode = ex.Message
                };

                // Redirect to the checkout error view:
                return RedirectToAction("Error", error);
            }

            return View(order);
        }

        public ActionResult Error(CheckoutErrorViewModel error)
        {
            return View(error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
