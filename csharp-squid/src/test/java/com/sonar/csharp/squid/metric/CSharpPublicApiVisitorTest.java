/*
 * Copyright (C) 2010 SonarSource SA
 * All rights reserved
 * mailto:contact AT sonarsource DOT com
 */
package com.sonar.csharp.squid.metric;

import static org.hamcrest.Matchers.is;
import static org.junit.Assert.assertThat;

import java.io.File;
import java.nio.charset.Charset;

import org.apache.commons.io.FileUtils;
import org.junit.Test;
import org.sonar.squid.Squid;
import org.sonar.squid.api.SourceProject;

import com.sonar.csharp.api.metric.CSharpMetric;
import com.sonar.csharp.squid.CSharpConfiguration;
import com.sonar.csharp.squid.scanner.CSharpAstScanner;

public class CSharpPublicApiVisitorTest {

  @Test
  public void testScanFile() {
    Squid squid = new Squid(new CSharpConfiguration(Charset.forName("UTF-8")));
    squid.register(CSharpAstScanner.class).scanFile(readFile("/metric/Money.cs"));
    SourceProject project = squid.decorateSourceCodeTreeWith(CSharpMetric.PUBLIC_API, CSharpMetric.PUBLIC_DOC_API);

    assertThat(project.getInt(CSharpMetric.PUBLIC_API), is(30));
    assertThat(project.getInt(CSharpMetric.PUBLIC_DOC_API), is(4));
  }

  @Test
  public void testScanInterface() {
    Squid squid = new Squid(new CSharpConfiguration(Charset.forName("UTF-8")));
    squid.register(CSharpAstScanner.class).scanFile(readFile("/metric/simpleInterface.cs"));
    SourceProject project = squid.decorateSourceCodeTreeWith(CSharpMetric.PUBLIC_API, CSharpMetric.PUBLIC_DOC_API);

    assertThat(project.getInt(CSharpMetric.PUBLIC_API), is(3));
    assertThat(project.getInt(CSharpMetric.PUBLIC_DOC_API), is(1));
  }

  protected File readFile(String path) {
    return FileUtils.toFile(getClass().getResource(path));
  }

}
