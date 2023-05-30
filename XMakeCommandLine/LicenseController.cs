using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommonUtilities;
using Forguncy.Common;
using Forguncy.Model;
using Forguncy.Properties;
using Forguncy.ServerApi.Common;
using Forguncy.UserService2.Models;
using Forguncy.UserService2.Provider;
using Forguncy.UserService2.Services.SecurityProviderServiceV2;
using ForguncyServerCommon;
using GrapeCity.Forguncy.ServerApi;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace Forguncy.UserService2.Controllers
{
	internal class LicenseManangementController
    {
		private class PageNumberInfoCreate : LicenseDataLoadBuilder
        {
            public override LicenseDataLoad Create(LicenseData licenseDatas, ServerLicenseModel model)
            {
                if (ResourceHelper.IsChinese())
                {
                    return new CnPageNumberInfoLoad(licenseDatas, model);
                }
                else
                {
                    return new JpKrPageNumberInfoLoad(licenseDatas, model);
                }
            }
        }
		
		private class JpKrPageNumberInfoLoad : PageNumberInfoLoad
		{
			public JpKrPageNumberInfoLoad(LicenseData licenseDatas, ServerLicenseModel model)
				: base(licenseDatas, model)
			{
			}
		
			public override void Load()
			{
				// ..
			}
		}
	}
}